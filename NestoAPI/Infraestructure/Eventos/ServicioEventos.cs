using NestoAPI.Infraestructure.Cupones;
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

        // NestoAPI#611 (Carlos, 14/10/26): el Nesto de #591 no conoce los campos nuevos. Para que un PUT suyo no los borre, se
        // apunta cuáles han venido en el cuerpo (Json.NET solo llama al setter de lo que viene) y el PUT conserva el resto.
        private readonly HashSet<string> recibidos = new HashSet<string>();
        private string almacen;
        private string productoCurso;
        private int? plazas;
        private string lugar;
        private string descripcionLarga;

        /// <summary>NestoAPI#611: ¿venía este campo en el cuerpo de la petición (aunque fuera null)?</summary>
        public bool Trae(string campo) => recibidos.Contains(campo);

        /// <summary>
        /// NestoAPI#611: la tienda donde es el evento (REI o ALC); la mantiene esa tienda. Opcional mientras haya clientes de
        /// #591: sin tienda, el evento se mantiene como en #591 (Tienda online, Dirección, Informática).
        /// </summary>
        public string Almacen { get => almacen; set { almacen = value; recibidos.Add(nameof(Almacen)); } }
        /// <summary>NestoAPI#611: la referencia de producto del curso (una por curso), con la que se factura en el corte 2.</summary>
        public string ProductoCurso { get => productoCurso; set { productoCurso = value; recibidos.Add(nameof(ProductoCurso)); } }
        /// <summary>NestoAPI#611: plazas del evento; null = sin límite.</summary>
        public int? Plazas { get => plazas; set { plazas = value; recibidos.Add(nameof(Plazas)); } }
        /// <summary>NestoAPI#611: solo lectura. Inscripciones que ocupan plaza (todas menos caducadas y anuladas).</summary>
        public int PlazasOcupadas { get; set; }
        /// <summary>NestoAPI#611 (para TNV).</summary>
        public string Lugar { get => lugar; set { lugar = value; recibidos.Add(nameof(Lugar)); } }
        /// <summary>
        /// NestoAPI#611 (para TNV): la columna Eventos.Descripcion. NO se llama «Descripcion» porque el EventoModel del Nesto de
        /// #591 ya manda una «Descripcion» calculada («Masterclass Cloasma 13/10/2026») y se guardaría como texto del evento.
        /// </summary>
        public string DescripcionLarga { get => descripcionLarga; set { descripcionLarga = value; recibidos.Add(nameof(DescripcionLarga)); } }
        /// <summary>NestoAPI#611: solo lectura. Cuándo se pasó lista (null = todavía no).</summary>
        public DateTime? FechaListaAsistencia { get; set; }
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
        /// <summary>NestoAPI#611: el AlmacénPedidoVta del usuario (para saber de qué tienda es). Null si no tiene.</summary>
        string AlmacenDelUsuario(IPrincipal usuario, string empresa);
        /// <summary>NestoAPI#611: la tienda del evento guardado ("" si no tiene); null si el evento no existe.</summary>
        Task<string> AlmacenDelEvento(int id);
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
    /// <see cref="CalculadoraEstadoSenalEvento"/>. Los eventos los mantiene cada tienda (NestoAPI#611, <see cref="PermisosEventos"/>);
    /// las señales las marca Administración.
    /// </summary>
    public class ServicioEventos : IServicioEventos
    {
        public const int LONGITUD_TITULO = 100;
        public const int LONGITUD_LUGAR = 100;
        public const int LONGITUD_DESCRIPCION = 2000;
        public const int LONGITUD_PRODUCTO = 15;

        /// <summary>NestoAPI#612: DELETE api/Eventos/Senales/{id} de una señal que ya facturó el curso o emitió el cupón (409).</summary>
        public const string ERROR_SENAL_YA_USADA = "SENAL_EVENTO_YA_USADA";

        public const string MENSAJE_SIN_PERMISO_SENALES =
            "Las señales de los eventos las marca Administración. Pídeselo a informática si necesitas hacerlo.";

        private static readonly string[] gruposSenales =
        {
            Constantes.GruposSeguridad.ADMINISTRACION,
            Constantes.GruposSeguridad.DIRECCION,
            Constantes.GruposSeguridad.INFORMATICA
        };

        private readonly NVEntities db;
        private readonly Func<DateTime> reloj;
        private readonly Func<string, string, string> almacenDelUsuario;
        private readonly IRepositorioFacturacionCursos facturacionCursos;
        private readonly IRepositorioCupones cupones;

        public ServicioEventos(NVEntities db) : this(db, () => DateTime.Now)
        {
        }

        /// <param name="almacenDelUsuario">(empresa, usuario sin dominio) → su AlmacénPedidoVta. Null: el de ParametrosUsuario.</param>
        internal ServicioEventos(NVEntities db, Func<DateTime> reloj, Func<string, string, string> almacenDelUsuario = null,
            IRepositorioFacturacionCursos facturacionCursos = null, IRepositorioCupones cupones = null)
        {
            this.db = db;
            this.reloj = reloj;
            this.almacenDelUsuario = almacenDelUsuario
                ?? ((empresa, usuario) => Controllers.ParametrosUsuarioController.LeerParametro(empresa, usuario, PermisosEventos.CLAVE_ALMACEN_USUARIO));
            this.facturacionCursos = facturacionCursos ?? new RepositorioFacturacionCursos(db);
            this.cupones = cupones ?? new RepositorioCupones(db);
        }

        public string AlmacenDelUsuario(IPrincipal usuario, string empresa)
        {
            string sinDominio = PermisosEventos.SinDominio(usuario?.Identity?.Name);
            return sinDominio == null ? null : almacenDelUsuario(EmpresaOPorDefecto(empresa), sinDominio)?.Trim();
        }

        public async Task<string> AlmacenDelEvento(int id)
        {
            Evento evento = await db.Eventos.SingleOrDefaultAsync(e => e.Id == id).ConfigureAwait(false);
            return evento == null ? null : (evento.Almacen?.Trim() ?? "");
        }

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
            Dictionary<int, int> ocupadas = await PlazasOcupadas(eventos.Select(e => e.Id).ToList()).ConfigureAwait(false);
            DateTime hoy = reloj().Date;
            return eventos.Where(e => e.Fecha.Date >= hoy).OrderBy(e => e.Fecha).ThenBy(e => e.Titulo)
                .Concat(eventos.Where(e => e.Fecha.Date < hoy).OrderByDescending(e => e.Fecha).ThenBy(e => e.Titulo))
                .Select(e => ADTO(e, ocupadas.TryGetValue(e.Id, out int n) ? n : 0))
                .ToList();
        }

        /// <summary>NestoAPI#611: cuántas inscripciones ocupan plaza en cada evento.</summary>
        private async Task<Dictionary<int, int>> PlazasOcupadas(List<int> ids)
        {
            if (!ids.Any())
            {
                return new Dictionary<int, int>();
            }
            List<string> estados = ReglasInscripcionEvento.OCUPAN_PLAZA.Select(e => e.ToString()).ToList();
            List<int> filas = await db.EventosInscripciones
                .Where(i => ids.Contains(i.EventoId) && estados.Contains(i.Estado))
                .Select(i => i.EventoId)
                .ToListAsync().ConfigureAwait(false);
            return filas.GroupBy(f => f).ToDictionary(g => g.Key, g => g.Count());
        }

        public async Task<EventoDTO> CrearEvento(EventoDTO evento, string usuario)
        {
            await Validar(evento).ConfigureAwait(false);
            var entidad = new Evento { Empresa = EmpresaOPorDefecto(evento.Empresa) };
            Copiar(evento, entidad, usuario);
            db.Eventos.Add(entidad);
            await db.SaveChangesAsync().ConfigureAwait(false);
            return ADTO(entidad, 0);
        }

        public async Task<EventoDTO> ModificarEvento(int id, EventoDTO evento, string usuario)
        {
            Evento entidad = await db.Eventos.SingleOrDefaultAsync(e => e.Id == id).ConfigureAwait(false)
                ?? throw NoEncontrado($"No existe el evento {id}.");
            if (evento != null)
            {
                evento.Empresa = Limpiar(entidad.Empresa);
            }
            await Validar(evento).ConfigureAwait(false);
            Dictionary<int, int> ocupadas = await PlazasOcupadas(new List<int> { id }).ConfigureAwait(false);
            int ocupadasEvento = ocupadas.TryGetValue(id, out int n) ? n : 0;
            if (evento.Trae(nameof(EventoDTO.Plazas)) && evento.Plazas.HasValue && evento.Plazas.Value < ocupadasEvento)
            {
                throw new NestoBusinessException(
                    $"Ya hay {ocupadasEvento} inscripciones: no se pueden dejar {evento.Plazas.Value} plazas. Anula antes las que sobren.");
            }
            Copiar(evento, entidad, usuario);
            await db.SaveChangesAsync().ConfigureAwait(false);
            return ADTO(entidad, ocupadasEvento);
        }

        private async Task Validar(EventoDTO evento)
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
            // NestoAPI#611 (Carlos, 14/10/26): cada evento es de una tienda, que es quien lo mantiene. Solo se exige si viene en
            // el cuerpo: el Nesto de #591 no lo manda y tiene que poder seguir creando y modificando eventos.
            if (evento.Trae(nameof(EventoDTO.Almacen)) && !PermisosEventos.EsAlmacenDeEventos(evento.Almacen))
            {
                throw new NestoBusinessException("Indica la tienda del evento: Reina (REI) o Alcobendas (ALC).");
            }
            if (evento.Plazas.HasValue && evento.Plazas.Value <= 0)
            {
                throw new NestoBusinessException("Las plazas tienen que ser más de cero (o ninguna, si no hay límite).");
            }
            if ((evento.Lugar?.Trim().Length ?? 0) > LONGITUD_LUGAR)
            {
                throw new NestoBusinessException($"El lugar no puede tener más de {LONGITUD_LUGAR} caracteres.");
            }
            if ((evento.DescripcionLarga?.Trim().Length ?? 0) > LONGITUD_DESCRIPCION)
            {
                throw new NestoBusinessException($"La descripción no puede tener más de {LONGITUD_DESCRIPCION} caracteres.");
            }
            string producto = Limpiar(evento.ProductoCurso);
            if (producto != null)
            {
                if (producto.Length > LONGITUD_PRODUCTO)
                {
                    throw new NestoBusinessException($"La referencia del curso no puede tener más de {LONGITUD_PRODUCTO} caracteres.");
                }
                string empresa = EmpresaOPorDefecto(evento.Empresa);
                bool existe = await db.Productos.AnyAsync(p => p.Empresa == empresa && p.Número == producto).ConfigureAwait(false);
                if (!existe)
                {
                    throw new NestoBusinessException($"No existe el producto {producto}: da de alta antes la referencia del curso.");
                }
            }
        }

        private void Copiar(EventoDTO origen, Evento destino, string usuario)
        {
            destino.Titulo = origen.Titulo.Trim();
            destino.Fecha = origen.Fecha.Date;
            destino.ImporteSenal = Math.Round(origen.ImporteSenal, 2, MidpointRounding.AwayFromZero);
            destino.Activo = origen.Activo;
            // NestoAPI#611: los campos nuevos, solo si vienen en el cuerpo (un PUT del Nesto de #591 no los borra).
            if (origen.Trae(nameof(EventoDTO.Almacen)))
            {
                destino.Almacen = Limpiar(origen.Almacen)?.ToUpperInvariant();
            }
            if (origen.Trae(nameof(EventoDTO.ProductoCurso)))
            {
                destino.ProductoCurso = Limpiar(origen.ProductoCurso);
            }
            if (origen.Trae(nameof(EventoDTO.Plazas)))
            {
                destino.Plazas = origen.Plazas;
            }
            if (origen.Trae(nameof(EventoDTO.Lugar)))
            {
                destino.Lugar = Limpiar(origen.Lugar);
            }
            if (origen.Trae(nameof(EventoDTO.DescripcionLarga)))
            {
                destino.Descripcion = Limpiar(origen.DescripcionLarga);
            }
            destino.Usuario = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            destino.FechaModificacion = reloj();
        }

        internal static EventoDTO ADTO(Evento e, int plazasOcupadas) => new EventoDTO
        {
            Id = e.Id,
            Empresa = Limpiar(e.Empresa),
            Titulo = e.Titulo?.Trim(),
            Fecha = e.Fecha.Date,
            ImporteSenal = e.ImporteSenal,
            Activo = e.Activo,
            Usuario = e.Usuario?.Trim(),
            FechaModificacion = e.FechaModificacion,
            Almacen = Limpiar(e.Almacen),
            ProductoCurso = Limpiar(e.ProductoCurso),
            Plazas = e.Plazas,
            PlazasOcupadas = plazasOcupadas,
            Lugar = e.Lugar?.Trim(),
            DescripcionLarga = e.Descripcion?.Trim(),
            FechaListaAsistencia = e.FechaListaAsistencia
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
            await ComprobarQueNoEstaUsada(senal).ConfigureAwait(false);
            db.EventosSenales.Remove(senal);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// NestoAPI#612 (Carlos, 16/10/26): una señal que ya ha servido para facturar el curso (y emitir el cupón) no se quita:
        /// 409 <see cref="ERROR_SENAL_YA_USADA"/>. Primero se rectifica la factura del curso (a mano) y se anula el cupón; si el
        /// cupón ya se usó, se dice. Sin factura ni cupón se quita como siempre.
        /// </summary>
        private async Task ComprobarQueNoEstaUsada(EventoSenal senal)
        {
            List<int> idsInscripciones = await db.EventosInscripciones.Where(i => i.EventoSenalId == senal.Id)
                .Select(i => i.Id).ToListAsync().ConfigureAwait(false);
            foreach (int inscripcionId in idsInscripciones)
            {
                FacturacionCursoInscripcion facturacion = await facturacionCursos.Leer(inscripcionId).ConfigureAwait(false);
                CuponLeido cupon = await cupones.LeerDeInscripcion(inscripcionId).ConfigureAwait(false);
                string mensaje = MensajeSenalUsada(Limpiar(facturacion?.Factura), cupon);
                if (mensaje != null)
                {
                    throw new NestoBusinessException(mensaje, new ErrorContext
                    {
                        ErrorCode = ERROR_SENAL_YA_USADA,
                        Empresa = Limpiar(senal.Empresa),
                        Cliente = Limpiar(senal.Cliente),
                        Factura = Limpiar(facturacion?.Factura)
                    }
                    .WithData("senal", senal.Id)
                    .WithData("inscripcion", inscripcionId)
                    .WithData("cupon", Limpiar(cupon?.Codigo)))
                    { StatusCode = HttpStatusCode.Conflict };
                }
            }
        }

        /// <summary>Null si la señal no se ha usado (ni factura del curso ni cupón).</summary>
        internal static string MensajeSenalUsada(string factura, CuponLeido cupon)
        {
            string codigo = Limpiar(cupon?.Codigo);
            if (factura == null && codigo == null)
            {
                return null;
            }
            string estado = cupon?.Estado?.Trim();
            bool anulado = estado == EstadosCupon.ANULADO;
            bool usado = estado == EstadosCupon.USADO;

            var usos = new List<string>();
            if (factura != null)
            {
                usos.Add($"facturar el curso (factura {factura})");
            }
            if (codigo != null)
            {
                usos.Add($"emitir el cupón {codigo}");
            }
            string mensaje = "Esta señal ya se ha usado para " + string.Join(" y ", usos) + (anulado ? ", que ya está anulado." : ".");
            if (usado)
            {
                string dondeSeUso = cupon.UsadoEnPedido.HasValue ? $" en el pedido {cupon.UsadoEnPedido}" : string.Empty;
                string facturaUso = Limpiar(cupon.FacturaUso);
                dondeSeUso += facturaUso != null ? $" (factura {facturaUso})" : string.Empty;
                mensaje += $" El cupón ya se ha usado{dondeSeUso}, así que no se puede anular.";
            }

            var pasos = new List<string>();
            if (factura != null)
            {
                pasos.Add("haga primero la rectificativa del curso");
            }
            if (codigo != null && !anulado && !usado)
            {
                pasos.Add(factura != null ? "anule el cupón" : "anule primero el cupón");
            }
            return pasos.Any() ? mensaje + " Para anularla, " + string.Join(" y ", pasos) + "." : mensaje;
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
