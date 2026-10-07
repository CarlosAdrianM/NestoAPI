using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>NestoAPI#591: un evento (curso, masterclass…) con señal reembolsable.</summary>
    public class EventoDTO
    {
        /// <summary>0 al crear.</summary>
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Titulo { get; set; }
        /// <summary>Desde ese día la señal está liberada (se ignora la hora).</summary>
        public DateTime Fecha { get; set; }
        /// <summary>Lo que se cobra para reservar la plaza (orientativo: la señal es lo que se cobró de verdad).</summary>
        public decimal ImporteSenal { get; set; }
        public bool Activo { get; set; } = true;
        /// <summary>Solo lectura (la API pone el del Identity).</summary>
        public string Usuario { get; set; }
        /// <summary>Solo lectura.</summary>
        public DateTime? FechaModificacion { get; set; }
    }

    /// <summary>NestoAPI#591: POST api/Eventos/{id}/Senales. El apunte a favor del extracto que es la señal del evento.</summary>
    public class MarcarSenalEventoDTO
    {
        public string Empresa { get; set; }
        /// <summary>ExtractoCliente.[Nº Orden].</summary>
        public int NumOrdenExtracto { get; set; }
        /// <summary>Opcional: si viene, el apunte tiene que ser de este cliente (lo manda el extracto de Nesto).</summary>
        public string Cliente { get; set; }
        /// <summary>Opcional: si viene (con Cliente), el apunte tiene que ser de este contacto.</summary>
        public string Contacto { get; set; }
    }

    /// <summary>NestoAPI#591: una señal con su evento, su cliente, lo que queda a favor y el estado calculado.</summary>
    public class SenalEventoDTO
    {
        public int Id { get; set; }
        public int EventoId { get; set; }
        public string Evento { get; set; }
        public DateTime FechaEvento { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public int NumOrdenExtracto { get; set; }
        public DateTime? FechaApunte { get; set; }
        public string Documento { get; set; }
        public string Concepto { get; set; }
        /// <summary>Importe original del apunte a favor (positivo).</summary>
        public decimal Importe { get; set; }
        /// <summary>Lo que el apunte sigue teniendo a favor hoy (positivo; 0 = consumida).</summary>
        public decimal ImportePendiente { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public EstadoSenalEvento Estado { get; set; }
        /// <summary>"Pendiente", "Liberada", "Sin compra", "Consumida".</summary>
        public string EstadoTexto { get; set; }
        /// <summary>Desde qué día pasa a «Sin compra» si no se consume.</summary>
        public DateTime FechaSinCompra { get; set; }
        /// <summary>Quién la marcó.</summary>
        public string Usuario { get; set; }
        /// <summary>Cuándo la marcó.</summary>
        public DateTime FechaMarcado { get; set; }
    }

    public interface IServicioEventos
    {
        Task<List<EventoDTO>> LeerEventos(string empresa, bool soloActivos);
        Task<EventoDTO> CrearEvento(EventoDTO evento, string usuario);
        Task<EventoDTO> ModificarEvento(int id, EventoDTO evento, string usuario);
        Task<List<SenalEventoDTO>> LeerSenales(string empresa, string estado, string cliente, int? eventoId);
        Task<List<SenalEventoDTO>> LeerSenalesCliente(string empresa, string cliente, string contacto);
        Task<SenalEventoDTO> MarcarSenal(int eventoId, MarcarSenalEventoDTO peticion, string usuario);
        Task QuitarSenal(int id);
    }

    /// <summary>
    /// NestoAPI#591 (MVP): eventos con señal reembolsable. La señal es un cobro a cuenta que queda a favor del cliente en su
    /// extracto (ImportePdte negativo); aquí solo se guarda qué apunte es la señal de qué evento, y el estado se calcula con
    /// <see cref="CalculadoraEstadoSenalEvento"/>. Los eventos los mantiene Tienda online; las señales las marca Administración.
    /// </summary>
    public class ServicioEventos : IServicioEventos
    {
        public const int LONGITUD_TITULO = 100;

        public const string MENSAJE_SIN_PERMISO_EVENTOS =
            "Los eventos los mantiene Tienda online. Pídeselo a informática si necesitas cambiarlos.";
        public const string MENSAJE_SIN_PERMISO_SENALES =
            "Las señales de los eventos las marca Administración. Pídeselo a informática si necesitas hacerlo.";

        private static readonly string[] gruposEventos =
        {
            Constantes.GruposSeguridad.TIENDA_ON_LINE,
            Constantes.GruposSeguridad.DIRECCION,
            Constantes.GruposSeguridad.INFORMATICA
        };

        private static readonly string[] gruposSenales =
        {
            Constantes.GruposSeguridad.ADMINISTRACION,
            Constantes.GruposSeguridad.DIRECCION,
            Constantes.GruposSeguridad.INFORMATICA
        };

        private readonly NVEntities db;
        private readonly Func<DateTime> reloj;

        public ServicioEventos(NVEntities db) : this(db, () => DateTime.Now)
        {
        }

        internal ServicioEventos(NVEntities db, Func<DateTime> reloj)
        {
            this.db = db;
            this.reloj = reloj;
        }

        public static bool PuedeMantenerEventos(IPrincipal usuario) =>
            usuario != null && gruposEventos.Any(g => usuario.IsInRoleSinDominio(g));

        public static bool PuedeMarcarSenales(IPrincipal usuario) =>
            usuario != null && gruposSenales.Any(g => usuario.IsInRoleSinDominio(g));

        // ---------------- Eventos ----------------

        /// <summary>Los de hoy en adelante primero (el más cercano antes) y luego los pasados (el más reciente antes).</summary>
        public async Task<List<EventoDTO>> LeerEventos(string empresa, bool soloActivos)
        {
            string empresaLimpia = EmpresaOPorDefecto(empresa);
            IQueryable<Evento> consulta = db.Eventos.Where(e => e.Empresa == empresaLimpia);
            if (soloActivos)
            {
                consulta = consulta.Where(e => e.Activo);
            }
            List<Evento> eventos = await consulta.ToListAsync().ConfigureAwait(false);
            DateTime hoy = reloj().Date;
            return eventos.Where(e => e.Fecha.Date >= hoy).OrderBy(e => e.Fecha).ThenBy(e => e.Titulo)
                .Concat(eventos.Where(e => e.Fecha.Date < hoy).OrderByDescending(e => e.Fecha).ThenBy(e => e.Titulo))
                .Select(ADTO)
                .ToList();
        }

        public async Task<EventoDTO> CrearEvento(EventoDTO evento, string usuario)
        {
            Validar(evento);
            var entidad = new Evento { Empresa = EmpresaOPorDefecto(evento.Empresa) };
            Copiar(evento, entidad, usuario);
            db.Eventos.Add(entidad);
            await db.SaveChangesAsync().ConfigureAwait(false);
            return ADTO(entidad);
        }

        public async Task<EventoDTO> ModificarEvento(int id, EventoDTO evento, string usuario)
        {
            Validar(evento);
            Evento entidad = await db.Eventos.SingleOrDefaultAsync(e => e.Id == id).ConfigureAwait(false)
                ?? throw NoEncontrado($"No existe el evento {id}.");
            Copiar(evento, entidad, usuario);
            await db.SaveChangesAsync().ConfigureAwait(false);
            return ADTO(entidad);
        }

        private static void Validar(EventoDTO evento)
        {
            if (evento == null)
            {
                throw new NestoBusinessException("Faltan los datos del evento.");
            }
            string titulo = evento.Titulo?.Trim();
            if (string.IsNullOrEmpty(titulo))
            {
                throw new NestoBusinessException("El evento tiene que tener un título.");
            }
            if (titulo.Length > LONGITUD_TITULO)
            {
                throw new NestoBusinessException($"El título del evento no puede tener más de {LONGITUD_TITULO} caracteres.");
            }
            if (evento.Fecha.Year < 2000)
            {
                throw new NestoBusinessException("El evento tiene que tener fecha.");
            }
            if (evento.ImporteSenal < 0)
            {
                throw new NestoBusinessException("El importe de la señal no puede ser negativo.");
            }
        }

        private void Copiar(EventoDTO origen, Evento destino, string usuario)
        {
            destino.Titulo = origen.Titulo.Trim();
            destino.Fecha = origen.Fecha.Date;
            destino.ImporteSenal = Math.Round(origen.ImporteSenal, 2, MidpointRounding.AwayFromZero);
            destino.Activo = origen.Activo;
            destino.Usuario = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            destino.FechaModificacion = reloj();
        }

        private static EventoDTO ADTO(Evento e) => new EventoDTO
        {
            Id = e.Id,
            Empresa = Limpiar(e.Empresa),
            Titulo = e.Titulo?.Trim(),
            Fecha = e.Fecha.Date,
            ImporteSenal = e.ImporteSenal,
            Activo = e.Activo,
            Usuario = e.Usuario?.Trim(),
            FechaModificacion = e.FechaModificacion
        };

        // ---------------- Señales ----------------

        public async Task<List<SenalEventoDTO>> LeerSenales(string empresa, string estado, string cliente, int? eventoId)
        {
            string empresaLimpia = EmpresaOPorDefecto(empresa);
            string clienteLimpio = Limpiar(cliente);
            IQueryable<EventoSenal> consulta = db.EventosSenales.Where(s => s.Empresa == empresaLimpia);
            if (clienteLimpio != null)
            {
                consulta = consulta.Where(s => s.Cliente == clienteLimpio);
            }
            if (eventoId.HasValue)
            {
                int id = eventoId.Value;
                consulta = consulta.Where(s => s.EventoId == id);
            }
            List<EventoSenal> senales = await consulta.ToListAsync().ConfigureAwait(false);
            List<SenalEventoDTO> completas = await Completar(senales, empresaLimpia).ConfigureAwait(false);

            EstadoSenalEvento? filtro = CalculadoraEstadoSenalEvento.Interpretar(estado);
            return completas
                .Where(s => filtro == null || s.Estado == filtro.Value)
                .OrderByDescending(s => s.FechaEvento).ThenBy(s => s.Evento).ThenBy(s => s.Nombre).ThenBy(s => s.Cliente)
                .ToList();
        }

        public async Task<List<SenalEventoDTO>> LeerSenalesCliente(string empresa, string cliente, string contacto)
        {
            string clienteLimpio = Limpiar(cliente) ?? throw new NestoBusinessException("Falta el cliente.");
            string contactoLimpio = Limpiar(contacto);
            List<SenalEventoDTO> senales = await LeerSenales(empresa, null, clienteLimpio, null).ConfigureAwait(false);
            return senales.Where(s => contactoLimpio == null || s.Contacto == contactoLimpio).ToList();
        }

        public async Task<SenalEventoDTO> MarcarSenal(int eventoId, MarcarSenalEventoDTO peticion, string usuario)
        {
            if (peticion == null || peticion.NumOrdenExtracto <= 0)
            {
                throw new NestoBusinessException("Falta el apunte del extracto que es la señal.");
            }
            string empresa = EmpresaOPorDefecto(peticion.Empresa);
            int numOrden = peticion.NumOrdenExtracto;

            Evento evento = await db.Eventos.SingleOrDefaultAsync(e => e.Id == eventoId).ConfigureAwait(false)
                ?? throw NoEncontrado($"No existe el evento {eventoId}.");
            if (Limpiar(evento.Empresa) != empresa)
            {
                throw new NestoBusinessException($"El evento «{evento.Titulo?.Trim()}» no es de la empresa {empresa}.");
            }

            ExtractoCliente apunte = await db.ExtractosCliente
                .SingleOrDefaultAsync(e => e.Empresa == empresa && e.Nº_Orden == numOrden).ConfigureAwait(false)
                ?? throw NoEncontrado($"No existe el apunte {numOrden} en el extracto de clientes.");

            string clienteApunte = Limpiar(apunte.Número);
            string contactoApunte = Limpiar(apunte.Contacto);
            string clientePedido = Limpiar(peticion.Cliente);
            string contactoPedido = Limpiar(peticion.Contacto);
            if ((clientePedido != null && clientePedido != clienteApunte) ||
                (clientePedido != null && contactoPedido != null && contactoPedido != contactoApunte))
            {
                throw new NestoBusinessException(
                    $"El apunte {numOrden} es del cliente {clienteApunte}/{contactoApunte}, no del {clientePedido}{(contactoPedido != null ? "/" + contactoPedido : "")}.");
            }
            if (apunte.ImportePdte >= 0)
            {
                throw new NestoBusinessException(
                    $"El apunte {numOrden} no tiene nada pendiente a favor del cliente: solo puede ser señal un cobro a cuenta sin liquidar.");
            }

            EventoSenal yaMarcada = await db.EventosSenales
                .FirstOrDefaultAsync(s => s.Empresa == empresa && s.NumOrdenExtracto == numOrden).ConfigureAwait(false);
            if (yaMarcada != null)
            {
                Evento otro = yaMarcada.EventoId == evento.Id
                    ? evento
                    : await db.Eventos.SingleOrDefaultAsync(e => e.Id == yaMarcada.EventoId).ConfigureAwait(false);
                throw new NestoBusinessException(
                    $"El apunte {numOrden} ya es la señal del evento «{otro?.Titulo?.Trim() ?? yaMarcada.EventoId.ToString()}». Quita esa señal antes si te has equivocado.");
            }

            var senal = new EventoSenal
            {
                EventoId = evento.Id,
                Empresa = empresa,
                Cliente = clienteApunte,
                Contacto = contactoApunte,
                NumOrdenExtracto = numOrden,
                // Carlos (07/10/26): el importe ORIGINAL del cobro a favor; lo pendiente se calcula al leer.
                Importe = Math.Round(apunte.Importe < 0 ? -apunte.Importe : -apunte.ImportePdte, 2, MidpointRounding.AwayFromZero),
                Usuario = UsuarioAuditoriaHelper.ParaAuditoria(usuario),
                FechaModificacion = reloj()
            };
            db.EventosSenales.Add(senal);
            await db.SaveChangesAsync().ConfigureAwait(false);

            string nombre = await NombreCliente(empresa, clienteApunte, contactoApunte).ConfigureAwait(false);
            return Componer(senal, evento, apunte, nombre, reloj().Date);
        }

        public async Task QuitarSenal(int id)
        {
            EventoSenal senal = await db.EventosSenales.SingleOrDefaultAsync(s => s.Id == id).ConfigureAwait(false)
                ?? throw NoEncontrado($"No existe la señal {id} (puede que ya la haya quitado otra persona).");
            db.EventosSenales.Remove(senal);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        /// <summary>Junta cada señal con su evento, su apunte del extracto y el nombre del cliente (en memoria: los char vienen con relleno).</summary>
        private async Task<List<SenalEventoDTO>> Completar(List<EventoSenal> senales, string empresa)
        {
            if (!senales.Any())
            {
                return new List<SenalEventoDTO>();
            }
            List<int> idsEventos = senales.Select(s => s.EventoId).Distinct().ToList();
            List<int> ordenes = senales.Select(s => s.NumOrdenExtracto).Distinct().ToList();
            List<string> clientes = senales.Select(s => Limpiar(s.Cliente)).Distinct().ToList();

            Dictionary<int, Evento> eventos = (await db.Eventos.Where(e => idsEventos.Contains(e.Id)).ToListAsync().ConfigureAwait(false))
                .ToDictionary(e => e.Id);
            Dictionary<int, ExtractoCliente> apuntes = (await db.ExtractosCliente
                .Where(e => e.Empresa == empresa && ordenes.Contains(e.Nº_Orden)).ToListAsync().ConfigureAwait(false))
                .GroupBy(e => e.Nº_Orden).ToDictionary(g => g.Key, g => g.First());
            var nombres = (await db.Clientes
                .Where(c => c.Empresa == empresa && clientes.Contains(c.Nº_Cliente))
                .Select(c => new { c.Nº_Cliente, c.Contacto, c.Nombre })
                .ToListAsync().ConfigureAwait(false))
                .GroupBy(c => Limpiar(c.Nº_Cliente) + "/" + Limpiar(c.Contacto))
                .ToDictionary(g => g.Key, g => g.First().Nombre?.Trim());

            DateTime hoy = reloj().Date;
            return senales.Select(s =>
            {
                eventos.TryGetValue(s.EventoId, out Evento evento);
                apuntes.TryGetValue(s.NumOrdenExtracto, out ExtractoCliente apunte);
                nombres.TryGetValue(Limpiar(s.Cliente) + "/" + Limpiar(s.Contacto), out string nombre);
                return Componer(s, evento, apunte, nombre, hoy);
            }).ToList();
        }

        private async Task<string> NombreCliente(string empresa, string cliente, string contacto)
        {
            var filas = await db.Clientes
                .Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente)
                .Select(c => new { c.Contacto, c.Nombre })
                .ToListAsync().ConfigureAwait(false);
            return filas.FirstOrDefault(c => Limpiar(c.Contacto) == contacto)?.Nombre?.Trim();
        }

        /// <summary>Sin apunte (borrado o de otra empresa) cuenta como consumida: ya no queda nada a favor.</summary>
        internal static SenalEventoDTO Componer(EventoSenal s, Evento evento, ExtractoCliente apunte, string nombre, DateTime hoy)
        {
            DateTime fechaEvento = evento?.Fecha.Date ?? DateTime.MinValue.Date;
            decimal importePdte = apunte?.ImportePdte ?? 0;
            EstadoSenalEvento estado = CalculadoraEstadoSenalEvento.Calcular(fechaEvento, importePdte, hoy);
            return new SenalEventoDTO
            {
                Id = s.Id,
                EventoId = s.EventoId,
                Evento = evento?.Titulo?.Trim(),
                FechaEvento = fechaEvento,
                Empresa = Limpiar(s.Empresa),
                Cliente = Limpiar(s.Cliente),
                Contacto = Limpiar(s.Contacto),
                Nombre = nombre,
                NumOrdenExtracto = s.NumOrdenExtracto,
                FechaApunte = apunte?.Fecha,
                Documento = apunte?.Nº_Documento?.Trim(),
                Concepto = apunte?.Concepto?.Trim(),
                Importe = s.Importe,
                ImportePendiente = importePdte < 0 ? -importePdte : 0,
                Estado = estado,
                EstadoTexto = CalculadoraEstadoSenalEvento.Texto(estado),
                FechaSinCompra = evento == null ? DateTime.MinValue.Date : CalculadoraEstadoSenalEvento.FechaSinCompra(fechaEvento),
                Usuario = s.Usuario?.Trim(),
                FechaMarcado = s.FechaModificacion
            };
        }

        private static NestoBusinessException NoEncontrado(string mensaje) =>
            new NestoBusinessException(mensaje) { StatusCode = HttpStatusCode.NotFound };

        private static string EmpresaOPorDefecto(string empresa) => Limpiar(empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;

        private static string Limpiar(string valor)
        {
            string limpio = valor?.Trim();
            return string.IsNullOrEmpty(limpio) ? null : limpio;
        }
    }
}
