using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
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
    /// <summary>
    /// NestoAPI#614: POST api/Eventos/CursoTienda. Nesto (CanalesExternos) ha importado de la tienda PrestaShop un pedido con la
    /// referencia de un curso: en vez de facturarlo como un pedido normal, lo pasa aquí como pago de la inscripción.
    /// </summary>
    public class CursoTiendaDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>La referencia del curso (Evento.ProductoCurso).</summary>
        public string Producto { get; set; }
        /// <summary>Lo que ha cobrado la tienda por el curso. Si no coincide con la señal, el corte 2 no lo automatiza y avisa.</summary>
        public decimal Importe { get; set; }
        /// <summary>Fecha del pedido de la tienda. Si no viene, hoy.</summary>
        public DateTime? Fecha { get; set; }
        /// <summary>El pedido de la tienda (id o referencia de PrestaShop), para los mensajes.</summary>
        public string ReferenciaTienda { get; set; }
        /// <summary>Opcional: Tarjeta (por defecto) o Transferencia.</summary>
        public string FormaPago { get; set; }
    }

    /// <summary>NestoAPI#611: POST api/Eventos/{id}/Inscripciones.</summary>
    public class InscribirEventoDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        /// <summary>Opcional: si no viene, el contacto principal del cliente.</summary>
        public string Contacto { get; set; }
        /// <summary>Nesto, NestoApp o Telefono (Teléfono). Con el token de un cliente de la tienda se ignora: TNV.</summary>
        public string Origen { get; set; }
        /// <summary>
        /// Opcional: cómo va a pagar (Tarjeta, Transferencia, Efectivo). Solo con Tarjeta (o sin decir nada) se genera el
        /// enlace de pago, y solo si <see cref="GenerarEnlacePago"/>.
        /// </summary>
        public string FormaPago { get; set; }
        /// <summary>Carlos (14/10/26): el enlace es opcional. Por defecto, sí.</summary>
        public bool GenerarEnlacePago { get; set; } = true;
        /// <summary>Opcional: si viene, ServicioPagos manda el enlace por correo al cliente.</summary>
        public string Correo { get; set; }
    }

    /// <summary>NestoAPI#611: POST api/Eventos/Inscripciones/{id}/Pago (marcar pagada a mano).</summary>
    public class MarcarPagoInscripcionDTO
    {
        /// <summary>Tarjeta, Transferencia o Efectivo.</summary>
        public string FormaPago { get; set; }
        /// <summary>Lo que se ha cobrado. Si no viene, el importe de la señal del evento.</summary>
        public decimal? Importe { get; set; }
        /// <summary>Cuándo se cobró. Si no viene, ahora. No puede ser futura.</summary>
        public DateTime? Fecha { get; set; }
    }

    /// <summary>NestoAPI#611: POST api/Eventos/{id}/Asistencia. Los Id de las inscripciones que vinieron.</summary>
    public class AsistenciaEventoDTO
    {
        public List<int> Asistentes { get; set; } = new List<int>();
    }

    /// <summary>NestoAPI#611: una inscripción con su evento y su cliente.</summary>
    public class InscripcionEventoDTO
    {
        public int Id { get; set; }
        public int EventoId { get; set; }
        public string Evento { get; set; }
        public DateTime FechaEvento { get; set; }
        public string Almacen { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public OrigenInscripcionEvento Origen { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public EstadoInscripcionEvento Estado { get; set; }
        /// <summary>«Asistió», «No asistió»… (con tildes, para pintar).</summary>
        public string EstadoTexto { get; set; }
        /// <summary>Hasta ese día (incluido) se puede pagar. Null si entró ya pagada.</summary>
        public DateTime? FechaLimitePago { get; set; }
        /// <summary>Mientras está Reservada, la prevista; luego, la real. Null si no se dijo.</summary>
        public string FormaPago { get; set; }
        public decimal? ImportePagado { get; set; }
        public DateTime? FechaPago { get; set; }
        public string UsuarioPago { get; set; }
        /// <summary>El enlace de pago (PagosTPV.Id del primero; los reintentos cuelgan de él).</summary>
        public int? PagoTPVId { get; set; }
        /// <summary>La página del enlace de pago, solo mientras se puede pagar (Reservada o Caducada).</summary>
        public string UrlPago { get; set; }
        /// <summary>La señal (#591) cuando el dinero entra por transferencia.</summary>
        public int? EventoSenalId { get; set; }
        public string UsuarioCreacion { get; set; }
        public DateTime FechaCreacion { get; set; }
        /// <summary>NestoAPI#612: la facturación del curso (pedido, factura, liquidación). Null si no ha empezado.</summary>
        public FacturacionCursoDTO Facturacion { get; set; }
    }

    /// <summary>
    /// NestoAPI#611: PUNTO DE ENGANCHE DEL CORTE 2 (#612). Se llama cada vez que una inscripción pasa a Pagada, venga el dinero
    /// por el enlace de pago (<see cref="ServicioInscripcionesEventos.RegistrarCobroEnlace"/>), por una transferencia o en
    /// efectivo (<see cref="ServicioInscripcionesEventos.MarcarPagada"/>) o por una señal sin inscripción previa
    /// (<see cref="ServicioInscripcionesEventos.InscribirDesdeTransferencia"/>). El corte 2 creará aquí el pedido del curso,
    /// la factura exenta, liquidará la señal, pasará la inscripción a Facturada y avisará (correo, push, nota en la ficha).
    /// Se llama con la inscripción ya guardada como Pagada; si lanza, la inscripción se queda Pagada y el error va a ELMAH.
    /// </summary>
    public interface IProcesadorInscripcionPagada
    {
        Task Procesar(EventoInscripcion inscripcion, Evento evento);
    }

    /// <summary>NestoAPI#611 (corte 1): todavía no hay nada que hacer al pagar. El corte 2 (#612) lo sustituye.</summary>
    public class ProcesadorInscripcionPagadaPendiente : IProcesadorInscripcionPagada
    {
        public Task Procesar(EventoInscripcion inscripcion, Evento evento) => Task.CompletedTask;
    }

    public interface IServicioInscripcionesEventos
    {
        Task<InscripcionEventoDTO> Inscribir(int eventoId, InscribirEventoDTO peticion, IPrincipal usuario);
        Task<List<InscripcionEventoDTO>> LeerInscripcionesEvento(int eventoId, IPrincipal usuario);
        Task<List<InscripcionEventoDTO>> LeerInscripcionesCliente(string empresa, string cliente, string contacto, IPrincipal usuario);
        Task<InscripcionEventoDTO> Anular(int id, IPrincipal usuario);
        Task<InscripcionEventoDTO> MarcarPagada(int id, MarcarPagoInscripcionDTO peticion, IPrincipal usuario);
        Task<List<InscripcionEventoDTO>> PasarLista(int eventoId, AsistenciaEventoDTO peticion, IPrincipal usuario);
        /// <summary>NestoAPI#612: relanzar a mano la facturación del curso desde donde se quedó.</summary>
        Task<InscripcionEventoDTO> FacturarCurso(int id, IPrincipal usuario);
        /// <summary>NestoAPI#612: una señal (#591) recién marcada paga (o crea) la inscripción del cliente en el evento.</summary>
        Task<InscripcionEventoDTO> InscribirDesdeTransferencia(int eventoSenalId, string usuario);
        /// <summary>NestoAPI#614: la compra del curso en la tienda PrestaShop paga (o crea) la inscripción del cliente.</summary>
        Task<InscripcionEventoDTO> InscribirDesdeTienda(CursoTiendaDTO peticion, IPrincipal usuario);
    }

    /// <summary>
    /// NestoAPI#611 (corte 1 de los cupones, #610): inscripciones a eventos SIN pedido. Al inscribir se genera (si se quiere)
    /// un enlace de pago con el evento como concepto y la inscripción como referencia (PagosTPV.Documento = «INS{id}»); la
    /// inscripción caduca a los 7 días sin pagar (o el día del evento, si es antes). Cuando el dinero llega —enlace,
    /// transferencia o efectivo—, pasa a Pagada y llama a <see cref="IProcesadorInscripcionPagada"/> (corte 2). El día del
    /// evento se pasa lista; si nadie lo hace, al día siguiente el job da a todos por asistidos y avisa a la tienda.
    /// </summary>
    public class ServicioInscripcionesEventos : IServicioInscripcionesEventos
    {
        /// <summary>PagosTPV.Documento del enlace de una inscripción: «INS» + Id (cabe en los 10 de Nº_Documento).</summary>
        public const string PREFIJO_DOCUMENTO_PAGO = "INS";
        public const string USUARIO_AUTOMATICO = "NestoAPI";
        private const string URL_PAGINA_PAGO = "https://api.nuevavision.es/pago/";

        private static readonly OrigenInscripcionEvento[] origenesDeLaCasa =
        {
            OrigenInscripcionEvento.Nesto, OrigenInscripcionEvento.NestoApp, OrigenInscripcionEvento.Telefono
        };

        private readonly NVEntities db;
        private readonly Func<DateTime> reloj;
        private readonly Func<IServicioPagos> pagos;
        private readonly Func<string, string, string> almacenDelUsuario;
        private readonly IProcesadorInscripcionPagada procesador;
        private readonly IRepositorioFacturacionCursos facturacion;
        private readonly Func<List<KeyValuePair<int, bool>>, string, Task> aplicarAsistenciaCupones;
        private readonly Func<bool> cursosTiendaEncendido;

        /// <summary>NestoAPI#612: en producción, el enganche es el que factura el curso (detrás de su interruptor).</summary>
        public ServicioInscripcionesEventos(NVEntities db)
            : this(db, () => DateTime.Now,
                () => new ServicioPagos(new RedsysService(), new ContabilidadService(), new LectorParametrosUsuario()),
                null, new ConfirmadorSenalEvento(db), new RepositorioFacturacionCursos(db),
                (asistencias, usuario) => new Cupones.ServicioCupones(db).AplicarAsistencia(asistencias, usuario))
        {
        }

        /// <param name="pagos">Para generar el enlace (perezoso: solo se crea si hace falta).</param>
        /// <param name="almacenDelUsuario">(empresa, usuario sin dominio) → su AlmacénPedidoVta. Null: el de ParametrosUsuario.</param>
        /// <param name="procesador">El enganche del corte 2. Null: <see cref="ProcesadorInscripcionPagadaPendiente"/>.</param>
        /// <param name="facturacion">NestoAPI#612: para enseñar en qué punto está la facturación del curso. Null: no se enseña.</param>
        /// <param name="aplicarAsistenciaCupones">NestoAPI#613: (inscripción → ¿vino?, usuario) anula o recupera su cupón. Null: nada.</param>
        internal ServicioInscripcionesEventos(NVEntities db, Func<DateTime> reloj, Func<IServicioPagos> pagos,
            Func<string, string, string> almacenDelUsuario = null, IProcesadorInscripcionPagada procesador = null,
            IRepositorioFacturacionCursos facturacion = null, Func<List<KeyValuePair<int, bool>>, string, Task> aplicarAsistenciaCupones = null,
            Func<bool> cursosTiendaEncendido = null)
        {
            this.aplicarAsistenciaCupones = aplicarAsistenciaCupones;
            this.cursosTiendaEncendido = cursosTiendaEncendido ?? (() => InterruptoresEventos.CursosDesdePrestashop);
            this.db = db;
            this.reloj = reloj ?? (() => DateTime.Now);
            this.pagos = pagos;
            this.almacenDelUsuario = almacenDelUsuario
                ?? ((empresa, usuario) => Controllers.ParametrosUsuarioController.LeerParametro(empresa, usuario, PermisosEventos.CLAVE_ALMACEN_USUARIO));
            this.procesador = procesador ?? new ProcesadorInscripcionPagadaPendiente();
            this.facturacion = facturacion;
        }

        // ---------------- Inscribir ----------------

        public async Task<InscripcionEventoDTO> Inscribir(int eventoId, InscribirEventoDTO peticion, IPrincipal usuario)
        {
            if (peticion == null || Limpiar(peticion.Cliente) == null)
            {
                throw new NestoBusinessException("Falta el cliente que se inscribe.");
            }
            string empresa = EmpresaOPorDefecto(peticion.Empresa);
            string cliente = Limpiar(peticion.Cliente);

            // El cliente de la tienda online solo se inscribe a sí mismo, y su origen es TNV diga lo que diga.
            string clienteDelToken = PermisosEventos.ClienteDelToken(usuario);
            OrigenInscripcionEvento origen;
            if (clienteDelToken != null)
            {
                if (clienteDelToken != cliente)
                {
                    throw Prohibido("Solo te puedes inscribir a ti mismo.");
                }
                origen = OrigenInscripcionEvento.TNV;
            }
            else
            {
                OrigenInscripcionEvento? leido = ReglasInscripcionEvento.Origen(peticion.Origen);
                if (!leido.HasValue || !origenesDeLaCasa.Contains(leido.Value))
                {
                    throw new NestoBusinessException("Indica desde dónde se inscribe: Nesto, NestoApp o Teléfono.");
                }
                origen = leido.Value;
            }

            FormaPagoInscripcionEvento? formaPago = null;
            if (Limpiar(peticion.FormaPago) != null)
            {
                formaPago = ReglasInscripcionEvento.FormaPago(peticion.FormaPago)
                    ?? throw new NestoBusinessException("La forma de pago tiene que ser Tarjeta, Transferencia o Efectivo.");
            }

            Evento evento = await LeerEvento(eventoId).ConfigureAwait(false);
            if (Limpiar(evento.Empresa) != empresa)
            {
                throw new NestoBusinessException($"El evento «{evento.Titulo?.Trim()}» no es de la empresa {empresa}.");
            }
            DateTime ahora = reloj();
            if (!evento.Activo)
            {
                throw new NestoBusinessException($"El evento «{evento.Titulo?.Trim()}» no está activo: no admite inscripciones.");
            }
            if (evento.Fecha.Date < ahora.Date)
            {
                throw new NestoBusinessException($"El evento «{evento.Titulo?.Trim()}» ya ha pasado.");
            }

            Cliente ficha = await LeerFichaCliente(empresa, cliente, Limpiar(peticion.Contacto)).ConfigureAwait(false);
            string contacto = Limpiar(ficha.Contacto);

            List<EventoInscripcion> delEvento = await db.EventosInscripciones
                .Where(i => i.EventoId == evento.Id).ToListAsync().ConfigureAwait(false);
            List<EventoInscripcion> ocupan = delEvento.Where(OcupaPlaza).ToList();
            if (ocupan.Any(i => Limpiar(i.Cliente) == cliente && Limpiar(i.Contacto) == contacto))
            {
                throw new NestoBusinessException($"El cliente {cliente}/{contacto} ya está inscrito en «{evento.Titulo?.Trim()}».");
            }
            if (evento.Plazas.HasValue && ocupan.Count >= evento.Plazas.Value)
            {
                throw new NestoBusinessException($"No quedan plazas en «{evento.Titulo?.Trim()}» ({evento.Plazas.Value} ocupadas).");
            }

            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(UsuarioAuditoriaHelper.Resolver(usuario, null));
            var inscripcion = new EventoInscripcion
            {
                EventoId = evento.Id,
                Empresa = empresa,
                Cliente = cliente,
                Contacto = contacto,
                Origen = origen.ToString(),
                Estado = EstadoInscripcionEvento.Reservada.ToString(),
                FechaLimitePago = ReglasInscripcionEvento.FechaLimitePago(ahora, evento.Fecha),
                FormaPago = formaPago?.ToString(),
                UsuarioCreacion = usuarioAuditoria,
                FechaCreacion = ahora,
                Usuario = usuarioAuditoria,
                FechaModificacion = ahora
            };
            db.EventosInscripciones.Add(inscripcion);
            await db.SaveChangesAsync().ConfigureAwait(false);

            string urlPago = null;
            bool conEnlace = peticion.GenerarEnlacePago && evento.ImporteSenal > 0
                && (formaPago == null || formaPago == FormaPagoInscripcionEvento.Tarjeta);
            if (conEnlace)
            {
                RespuestaIniciarPago enlace;
                try
                {
                    enlace = await pagos().IniciarPago(new SolicitudPagoTPV
                    {
                        Empresa = empresa,
                        Cliente = cliente,
                        Contacto = contacto,
                        Importe = evento.ImporteSenal,
                        Descripcion = ConceptoPago(evento),
                        Correo = Limpiar(peticion.Correo),
                        Documento = DocumentoPago(inscripcion.Id),
                        Vendedor = Limpiar(ficha.Vendedor)
                    }, UsuarioAuditoriaHelper.Resolver(usuario, usuarioAuditoria)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Sin enlace no hay inscripción a medias: se quita y se cuenta por qué.
                    db.EventosInscripciones.Remove(inscripcion);
                    await db.SaveChangesAsync().ConfigureAwait(false);
                    if (ex is NestoBusinessException)
                    {
                        throw;
                    }
                    throw new NestoBusinessException($"No se ha podido generar el enlace de pago: {ex.Message}");
                }
                inscripcion.PagoTPVId = enlace.IdPago;
                inscripcion.FormaPago = FormaPagoInscripcionEvento.Tarjeta.ToString();
                await db.SaveChangesAsync().ConfigureAwait(false);
                urlPago = enlace.UrlPaginaPago;
            }

            InscripcionEventoDTO dto = Componer(inscripcion, evento, ficha.Nombre?.Trim());
            dto.UrlPago = urlPago;
            return dto;
        }

        internal static string ConceptoPago(Evento evento) => $"Señal {evento.Titulo?.Trim()}";

        internal static string DocumentoPago(int inscripcionId) => PREFIJO_DOCUMENTO_PAGO + inscripcionId;

        // ---------------- Leer ----------------

        public async Task<List<InscripcionEventoDTO>> LeerInscripcionesEvento(int eventoId, IPrincipal usuario)
        {
            if (PermisosEventos.ClienteDelToken(usuario) != null)
            {
                throw Prohibido("La lista de inscritos de un evento solo la ve la casa.");
            }
            Evento evento = await LeerEvento(eventoId).ConfigureAwait(false);
            List<EventoInscripcion> inscripciones = await db.EventosInscripciones
                .Where(i => i.EventoId == eventoId).ToListAsync().ConfigureAwait(false);
            List<InscripcionEventoDTO> lista = await Completar(inscripciones, new Dictionary<int, Evento> { [evento.Id] = evento }).ConfigureAwait(false);
            return lista.OrderBy(i => OrdenEstado(i.Estado)).ThenBy(i => i.Nombre).ThenBy(i => i.Cliente).ToList();
        }

        public async Task<List<InscripcionEventoDTO>> LeerInscripcionesCliente(string empresa, string cliente, string contacto, IPrincipal usuario)
        {
            string clienteLimpio = Limpiar(cliente) ?? throw new NestoBusinessException("Falta el cliente.");
            string clienteDelToken = PermisosEventos.ClienteDelToken(usuario);
            if (clienteDelToken != null && clienteDelToken != clienteLimpio)
            {
                throw Prohibido("Solo puedes ver tus inscripciones.");
            }
            string empresaLimpia = EmpresaOPorDefecto(empresa);
            string contactoLimpio = Limpiar(contacto);
            List<EventoInscripcion> inscripciones = (await db.EventosInscripciones
                .Where(i => i.Empresa == empresaLimpia && i.Cliente == clienteLimpio).ToListAsync().ConfigureAwait(false))
                .Where(i => contactoLimpio == null || Limpiar(i.Contacto) == contactoLimpio)
                .ToList();
            List<InscripcionEventoDTO> lista = await Completar(inscripciones, null).ConfigureAwait(false);
            return lista.OrderByDescending(i => i.FechaEvento).ThenByDescending(i => i.FechaCreacion).ToList();
        }

        // ---------------- Anular ----------------

        /// <summary>
        /// Solo una inscripción sin pagar (Reservada). Pagada no se anula: las normas del evento no devuelven la señal, y el
        /// dinero ya está contabilizado. La puede anular cualquiera de la casa y el propio cliente desde la tienda.
        /// </summary>
        public async Task<InscripcionEventoDTO> Anular(int id, IPrincipal usuario)
        {
            EventoInscripcion inscripcion = await LeerInscripcion(id).ConfigureAwait(false);
            string clienteDelToken = PermisosEventos.ClienteDelToken(usuario);
            if (clienteDelToken != null && clienteDelToken != Limpiar(inscripcion.Cliente))
            {
                throw Prohibido("Solo puedes anular tus inscripciones.");
            }
            EstadoInscripcionEvento estado = EstadoDe(inscripcion);
            if (estado != EstadoInscripcionEvento.Reservada)
            {
                throw new NestoBusinessException(
                    $"La inscripción está {ReglasInscripcionEvento.Texto(estado).ToLowerInvariant()}: solo se puede anular una inscripción sin pagar.");
            }
            Evento evento = await LeerEvento(inscripcion.EventoId).ConfigureAwait(false);
            Cambiar(inscripcion, EstadoInscripcionEvento.Anulada, UsuarioDe(usuario));
            await db.SaveChangesAsync().ConfigureAwait(false);
            return Componer(inscripcion, evento, null);
        }

        // ---------------- Pagos ----------------

        /// <summary>
        /// Carlos (14/10/26): marcar a mano que la inscripción está pagada (transferencia, tarjeta en el datáfono o efectivo en la
        /// tienda). Quien mantiene el evento, Administración (cualquier evento) y Dirección. NO apunta nada en contabilidad.
        /// <para>Efectivo (Carlos, 15/10/26): el cobro en efectivo no se automatiza; lo mete cada usuario en su caja, como
        /// cualquier cobro en efectivo. La inscripción en efectivo se queda Reservada hasta que alguien (la tienda o
        /// Administración) la marca aquí como pagada, igual que las demás formas de pago.</para>
        /// </summary>
        public async Task<InscripcionEventoDTO> MarcarPagada(int id, MarcarPagoInscripcionDTO peticion, IPrincipal usuario)
        {
            EventoInscripcion inscripcion = await LeerInscripcion(id).ConfigureAwait(false);
            Evento evento = await LeerEvento(inscripcion.EventoId).ConfigureAwait(false);
            ComprobarPuedeMantener(usuario, evento, paraMarcarPagada: true);

            FormaPagoInscripcionEvento forma = ReglasInscripcionEvento.FormaPago(peticion?.FormaPago)
                ?? throw new NestoBusinessException("Indique cómo ha pagado: Tarjeta, Transferencia o Efectivo.");
            decimal importe = Math.Round(peticion.Importe ?? evento.ImporteSenal, 2, MidpointRounding.AwayFromZero);
            if (importe < 0)
            {
                throw new NestoBusinessException("El importe pagado no puede ser negativo.");
            }
            DateTime ahora = reloj();
            DateTime fecha = peticion.Fecha ?? ahora;
            if (fecha.Date > ahora.Date)
            {
                throw new NestoBusinessException("La fecha del pago no puede ser futura.");
            }

            await Pagar(inscripcion, evento, forma, importe, fecha, UsuarioDe(usuario), null).ConfigureAwait(false);
            return Componer(inscripcion, evento, null);
        }

        /// <summary>
        /// Lo llama ServicioPagos cuando Redsys confirma un enlace cuyo Documento empieza por «INS»: la inscripción de ese
        /// enlace pasa a Pagada. <paramref name="pagoRaizId"/> es el PagoTPV original (los reintentos llevan PagoOriginalId).
        /// Idempotente: si ya estaba pagada, no hace nada. Devuelve null si no hay inscripción con ese enlace.
        /// </summary>
        public async Task<InscripcionEventoDTO> RegistrarCobroEnlace(int pagoRaizId, decimal importe)
        {
            EventoInscripcion inscripcion = await db.EventosInscripciones
                .FirstOrDefaultAsync(i => i.PagoTPVId == pagoRaizId).ConfigureAwait(false);
            if (inscripcion == null)
            {
                return null;
            }
            Evento evento = await LeerEvento(inscripcion.EventoId).ConfigureAwait(false);
            EstadoInscripcionEvento estado = EstadoDe(inscripcion);
            if (estado == EstadoInscripcionEvento.Anulada)
            {
                ElmahHelper.Log(new Exception($"[Inscripciones #611] Se ha cobrado el enlace {pagoRaizId} de la inscripción {inscripcion.Id} " +
                    $"(cliente {Limpiar(inscripcion.Cliente)}, evento «{evento.Titulo?.Trim()}»), pero estaba anulada: el cobro está en su " +
                    "extracto; decidid si se reactiva (marcándola pagada) o se devuelve."));
                return Componer(inscripcion, evento, null);
            }
            if (!ReglasInscripcionEvento.PuedePasar(estado, EstadoInscripcionEvento.Pagada) || estado == EstadoInscripcionEvento.Pagada)
            {
                return Componer(inscripcion, evento, null);
            }
            await Pagar(inscripcion, evento, FormaPagoInscripcionEvento.Tarjeta, importe, reloj(), USUARIO_AUTOMATICO, null).ConfigureAwait(false);
            return Componer(inscripcion, evento, null);
        }

        /// <summary>
        /// NestoAPI#611: llega una señal (#591) por transferencia. Si el cliente tenía una inscripción sin pagar en el evento,
        /// pasa a Pagada con esa señal; si no tenía ninguna, se crea con origen Transferencia, ya pagada. Si ya estaba pagada,
        /// solo se le apunta la señal. Pensado para el corte 2 (#612), que es quien lo engancha al marcar la señal.
        /// </summary>
        public async Task<InscripcionEventoDTO> InscribirDesdeTransferencia(int eventoSenalId, string usuario)
        {
            EventoSenal senal = await db.EventosSenales.SingleOrDefaultAsync(s => s.Id == eventoSenalId).ConfigureAwait(false)
                ?? throw NoEncontrado($"No existe la señal {eventoSenalId}.");
            Evento evento = await LeerEvento(senal.EventoId).ConfigureAwait(false);
            string cliente = Limpiar(senal.Cliente);
            string contacto = Limpiar(senal.Contacto);
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            DateTime ahora = reloj();
            EventoInscripcion inscripcion = await PagarOInscribir(evento, Limpiar(senal.Empresa), cliente, contacto, OrigenInscripcionEvento.Transferencia,
                FormaPagoInscripcionEvento.Transferencia, senal.Importe, ahora, usuarioAuditoria, senal.Id).ConfigureAwait(false);
            return Componer(inscripcion, evento, null);
        }

        /// <summary>
        /// NestoAPI#614: Nesto ha importado de la tienda PrestaShop la compra de un curso (la referencia de Evento.ProductoCurso de un
        /// evento activo y por venir). En vez de un pedido normal, paga la inscripción del cliente (o la crea con origen PrestaShop)
        /// y de ahí sigue el corte 2: pedido del curso, factura exenta y cupón. Sin enlace de pago, la factura queda pendiente de
        /// cobro (SinCobro) y se cobra como los demás pedidos de la tienda. Idempotente: si ya estaba pagada, se devuelve tal cual.
        /// </summary>
        public async Task<InscripcionEventoDTO> InscribirDesdeTienda(CursoTiendaDTO peticion, IPrincipal usuario)
        {
            if (!cursosTiendaEncendido())
            {
                throw new NestoBusinessException($"La compra de cursos desde la tienda PrestaShop está apagada ({InterruptoresEventos.CLAVE_CURSOS_TIENDA}).");
            }
            if (usuario == null || PermisosEventos.ClienteDelToken(usuario) != null)
            {
                throw Prohibido("Solo la casa puede pasar la compra de un curso de la tienda.");
            }
            if (peticion == null || Limpiar(peticion.Cliente) == null)
            {
                throw new NestoBusinessException("Falta el cliente que ha comprado el curso.");
            }
            string producto = Limpiar(peticion.Producto) ?? throw new NestoBusinessException("Falta la referencia del curso.");
            string empresa = EmpresaOPorDefecto(peticion.Empresa);
            string cliente = Limpiar(peticion.Cliente);
            FormaPagoInscripcionEvento forma = FormaPagoInscripcionEvento.Tarjeta;
            if (Limpiar(peticion.FormaPago) != null)
            {
                forma = ReglasInscripcionEvento.FormaPago(peticion.FormaPago) ?? throw new NestoBusinessException(
                    "La forma de pago tiene que ser Tarjeta o Transferencia.");
            }
            decimal importe = Math.Round(peticion.Importe, 2, MidpointRounding.AwayFromZero);
            if (importe < 0)
            {
                throw new NestoBusinessException("El importe del curso no puede ser negativo.");
            }
            DateTime ahora = reloj();
            DateTime fecha = peticion.Fecha ?? ahora;
            string pedidoTienda = Limpiar(peticion.ReferenciaTienda) == null ? "" : $" (pedido {Limpiar(peticion.ReferenciaTienda)} de la tienda)";

            // La referencia, en memoria: los char vienen con relleno (los eventos activos son pocos)
            List<Evento> eventos = (await db.Eventos.Where(e => e.Empresa == empresa && e.Activo && e.ProductoCurso != null)
                .ToListAsync().ConfigureAwait(false))
                .Where(e => Limpiar(e.ProductoCurso) == producto && e.Fecha.Date >= fecha.Date)
                .ToList();
            if (!eventos.Any())
            {
                throw NoEncontrado($"No hay ningún evento activo y por venir con la referencia {producto}{pedidoTienda}: revise el pedido a mano.");
            }
            if (eventos.Count > 1)
            {
                throw new NestoBusinessException($"Hay {eventos.Count} eventos activos con la referencia {producto} (" +
                    string.Join(", ", eventos.OrderBy(e => e.Fecha).Select(e => $"«{e.Titulo?.Trim()}» del {e.Fecha:dd/MM/yyyy}")) +
                    $"): cada curso tiene que tener su referencia. Revise el pedido{pedidoTienda} a mano.");
            }
            Evento evento = eventos[0];
            Cliente ficha = await LeerFichaCliente(empresa, cliente, Limpiar(peticion.Contacto)).ConfigureAwait(false);
            EventoInscripcion inscripcion = await PagarOInscribir(evento, empresa, cliente, Limpiar(ficha.Contacto), OrigenInscripcionEvento.PrestaShop,
                forma, importe, fecha, UsuarioDe(usuario), null).ConfigureAwait(false);
            return (await Completar(new List<EventoInscripcion> { inscripcion }, new Dictionary<int, Evento> { [evento.Id] = evento })
                .ConfigureAwait(false)).Single();
        }

        /// <summary>
        /// El núcleo de las inscripciones que llegan ya pagadas (señal por transferencia, compra en la tienda): si el cliente ya
        /// estaba pagado en el evento, esa (con la señal apuntada si no la tenía); si tenía una sin pagar, se paga; si no tenía
        /// ninguna, se crea con <paramref name="origen"/> y se paga.
        /// </summary>
        private async Task<EventoInscripcion> PagarOInscribir(Evento evento, string empresa, string cliente, string contacto, OrigenInscripcionEvento origen,
            FormaPagoInscripcionEvento forma, decimal importe, DateTime fecha, string usuarioAuditoria, int? eventoSenalId)
        {
            DateTime ahora = reloj();
            List<EventoInscripcion> delCliente = (await db.EventosInscripciones
                .Where(i => i.EventoId == evento.Id && i.Cliente == cliente).ToListAsync().ConfigureAwait(false))
                .Where(i => Limpiar(i.Contacto) == contacto)
                .ToList();

            EventoInscripcion yaPagada = delCliente.FirstOrDefault(i => EstadoDe(i) != EstadoInscripcionEvento.Reservada
                && EstadoDe(i) != EstadoInscripcionEvento.Caducada && EstadoDe(i) != EstadoInscripcionEvento.Anulada);
            if (yaPagada != null)
            {
                if (eventoSenalId.HasValue && yaPagada.EventoSenalId == null)
                {
                    yaPagada.EventoSenalId = eventoSenalId;
                    yaPagada.Usuario = usuarioAuditoria;
                    yaPagada.FechaModificacion = ahora;
                    await db.SaveChangesAsync().ConfigureAwait(false);
                }
                return yaPagada;
            }

            EventoInscripcion pendiente = delCliente
                .Where(i => EstadoDe(i) == EstadoInscripcionEvento.Reservada || EstadoDe(i) == EstadoInscripcionEvento.Caducada)
                .OrderBy(i => EstadoDe(i) == EstadoInscripcionEvento.Reservada ? 0 : 1).ThenByDescending(i => i.FechaCreacion)
                .FirstOrDefault();
            if (pendiente == null)
            {
                pendiente = new EventoInscripcion
                {
                    EventoId = evento.Id,
                    Empresa = empresa,
                    Cliente = cliente,
                    Contacto = contacto,
                    Origen = origen.ToString(),
                    Estado = EstadoInscripcionEvento.Reservada.ToString(),
                    FechaLimitePago = null,
                    UsuarioCreacion = usuarioAuditoria,
                    FechaCreacion = ahora,
                    Usuario = usuarioAuditoria,
                    FechaModificacion = ahora
                };
                db.EventosInscripciones.Add(pendiente);
            }
            await Pagar(pendiente, evento, forma, importe, fecha, usuarioAuditoria, eventoSenalId).ConfigureAwait(false);
            return pendiente;
        }

        /// <summary>Pasa a Pagada, guarda y llama al enganche del corte 2 (sus fallos no deshacen el pago: van a ELMAH).</summary>
        private async Task Pagar(EventoInscripcion inscripcion, Evento evento, FormaPagoInscripcionEvento forma, decimal importe,
            DateTime fecha, string usuario, int? eventoSenalId)
        {
            EstadoInscripcionEvento estado = EstadoDe(inscripcion);
            if (!ReglasInscripcionEvento.PuedePasar(estado, EstadoInscripcionEvento.Pagada) || estado == EstadoInscripcionEvento.Pagada)
            {
                throw new NestoBusinessException(
                    $"La inscripción está {ReglasInscripcionEvento.Texto(estado).ToLowerInvariant()}: no se puede marcar como pagada.");
            }
            Cambiar(inscripcion, EstadoInscripcionEvento.Pagada, usuario);
            inscripcion.FormaPago = forma.ToString();
            inscripcion.ImportePagado = importe;
            inscripcion.FechaPago = fecha;
            inscripcion.UsuarioPago = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            if (eventoSenalId.HasValue)
            {
                inscripcion.EventoSenalId = eventoSenalId;
            }
            await db.SaveChangesAsync().ConfigureAwait(false);

            try
            {
                await procesador.Procesar(inscripcion, evento).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Inscripciones #611] La inscripción {inscripcion.Id} está pagada, pero ha fallado lo que " +
                    $"viene después (corte 2): {ex.Message}", ex));
            }
        }

        // ---------------- NestoAPI#612: facturación del curso ----------------

        /// <summary>
        /// Relanza a mano la facturación del curso (pedido, factura exenta, liquidación de la señal) desde donde se quedó: lo
        /// hecho no se repite. Quien puede marcar pagos (la tienda del evento, Administración, Dirección). 400 con el motivo
        /// si no se puede automatizar o si falla; con el interruptor apagado, 400.
        /// </summary>
        public async Task<InscripcionEventoDTO> FacturarCurso(int id, IPrincipal usuario)
        {
            EventoInscripcion inscripcion = await LeerInscripcion(id).ConfigureAwait(false);
            Evento evento = await LeerEvento(inscripcion.EventoId).ConfigureAwait(false);
            ComprobarPuedeMantener(usuario, evento, paraMarcarPagada: true);
            if (!(procesador is IConfirmadorSenalEvento confirmador))
            {
                throw new NestoBusinessException("La facturación automática de los cursos no está disponible.");
            }
            _ = await confirmador.ConfirmarSenal(inscripcion, evento, aMano: true).ConfigureAwait(false);
            return (await Completar(new List<EventoInscripcion> { inscripcion }, new Dictionary<int, Evento> { [evento.Id] = evento })
                .ConfigureAwait(false)).Single();
        }

        /// <summary>
        /// Lo que no terminó (job diario): las que fallaron, desde donde se quedaron, hasta <see cref="ConfirmadorSenalEvento.MAX_INTENTOS_AUTOMATICOS"/>
        /// intentos; y las facturadas sin cupón (corte 3). Devuelve cuántas se han relanzado.
        /// </summary>
        internal async Task<int> ReintentarFacturacionCursos()
        {
            if (facturacion == null || !(procesador is ConfirmadorSenalEvento confirmador) || !confirmador.Encendido)
            {
                return 0;
            }
            List<int> ids = (await facturacion.ConError(ConfirmadorSenalEvento.MAX_INTENTOS_AUTOMATICOS).ConfigureAwait(false) ?? new List<int>())
                .Concat(await facturacion.FacturadasSinCupon().ConfigureAwait(false) ?? new List<int>())
                .Distinct().ToList();
            int relanzadas = 0;
            foreach (int id in ids)
            {
                try
                {
                    EventoInscripcion inscripcion = await db.EventosInscripciones.SingleOrDefaultAsync(i => i.Id == id).ConfigureAwait(false);
                    if (inscripcion == null || !ReglasInscripcionEvento.EN_LISTA_ASISTENCIA.Contains(EstadoDe(inscripcion)))
                    {
                        continue;
                    }
                    Evento evento = await LeerEvento(inscripcion.EventoId).ConfigureAwait(false);
                    _ = await confirmador.ConfirmarSenal(inscripcion, evento, aMano: false).ConfigureAwait(false);
                    relanzadas++;
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Cursos #612] No se ha podido relanzar la facturación del curso de la inscripción {id}: {ex.Message}", ex));
                }
            }
            return relanzadas;
        }

        // ---------------- Lista de asistencia ----------------

        /// <summary>
        /// La tienda del evento (o Dirección) pasa lista desde el día del evento: las inscripciones pagadas que vienen en
        /// <see cref="AsistenciaEventoDTO.Asistentes"/> pasan a Asistio y el resto de pagadas a NoAsistio (pierden el cupón,
        /// corte 3). Se puede repetir para corregirla.
        /// </summary>
        public async Task<List<InscripcionEventoDTO>> PasarLista(int eventoId, AsistenciaEventoDTO peticion, IPrincipal usuario)
        {
            Evento evento = await LeerEvento(eventoId).ConfigureAwait(false);
            ComprobarPuedeMantener(usuario, evento);
            DateTime ahora = reloj();
            if (evento.Fecha.Date > ahora.Date)
            {
                throw new NestoBusinessException($"El evento «{evento.Titulo?.Trim()}» es el {evento.Fecha:dd/MM/yyyy}: la lista se pasa desde ese día.");
            }

            HashSet<int> vinieron = new HashSet<int>(peticion?.Asistentes ?? new List<int>());
            List<EventoInscripcion> inscripciones = await db.EventosInscripciones
                .Where(i => i.EventoId == eventoId).ToListAsync().ConfigureAwait(false);
            List<EventoInscripcion> enLista = inscripciones
                .Where(i => ReglasInscripcionEvento.EN_LISTA_ASISTENCIA.Contains(EstadoDe(i))).ToList();

            List<int> ajenas = vinieron.Where(id => !inscripciones.Any(i => i.Id == id)).ToList();
            if (ajenas.Any())
            {
                throw new NestoBusinessException($"Estas inscripciones no son del evento «{evento.Titulo?.Trim()}»: {string.Join(", ", ajenas)}.");
            }
            List<EventoInscripcion> sinPagar = inscripciones.Where(i => vinieron.Contains(i.Id) && !enLista.Contains(i)).ToList();
            if (sinPagar.Any())
            {
                throw new NestoBusinessException("Estas inscripciones no están pagadas; márcalas como pagadas antes de pasar lista: " +
                    string.Join(", ", sinPagar.Select(i => $"{i.Id} ({Limpiar(i.Cliente)}, {ReglasInscripcionEvento.Texto(EstadoDe(i)).ToLowerInvariant()})")) + ".");
            }

            string usuarioAuditoria = UsuarioDe(usuario);
            foreach (EventoInscripcion inscripcion in enLista)
            {
                Cambiar(inscripcion, vinieron.Contains(inscripcion.Id) ? EstadoInscripcionEvento.Asistio : EstadoInscripcionEvento.NoAsistio,
                    usuarioAuditoria);
            }
            evento.FechaListaAsistencia = ahora;
            await db.SaveChangesAsync().ConfigureAwait(false);

            // NestoAPI#613: quien no vino pierde el cupón; si se corrige la lista y sí vino, lo recupera. Si falla, la lista
            // queda pasada igual y el error va a ELMAH.
            if (aplicarAsistenciaCupones != null)
            {
                try
                {
                    await aplicarAsistenciaCupones(enLista.Select(i => new KeyValuePair<int, bool>(i.Id, vinieron.Contains(i.Id))).ToList(),
                        usuarioAuditoria).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Cupones #613] La lista del evento {evento.Id} está pasada, pero no se han podido anular o " +
                        $"recuperar los cupones: {ex.Message}", ex));
                }
            }

            return (await Completar(inscripciones, new Dictionary<int, Evento> { [evento.Id] = evento }).ConfigureAwait(false))
                .OrderBy(i => OrdenEstado(i.Estado)).ThenBy(i => i.Nombre).ToList();
        }

        // ---------------- Jobs ----------------

        /// <summary>Las Reservada con la fecha límite pasada pasan a Caducada. Devuelve las caducadas (para avisar a quien las creó).</summary>
        internal async Task<List<InscripcionEventoDTO>> CaducarVencidas()
        {
            DateTime ahora = reloj();
            DateTime hoy = ahora.Date;
            string reservada = EstadoInscripcionEvento.Reservada.ToString();
            List<EventoInscripcion> vencidas = (await db.EventosInscripciones
                .Where(i => i.Estado == reservada && i.FechaLimitePago < hoy).ToListAsync().ConfigureAwait(false))
                .Where(i => ReglasInscripcionEvento.EstaCaducada(i.FechaLimitePago, hoy))
                .ToList();
            if (!vencidas.Any())
            {
                return new List<InscripcionEventoDTO>();
            }
            foreach (EventoInscripcion inscripcion in vencidas)
            {
                Cambiar(inscripcion, EstadoInscripcionEvento.Caducada, USUARIO_AUTOMATICO);
            }
            await db.SaveChangesAsync().ConfigureAwait(false);
            return await Completar(vencidas, null).ConfigureAwait(false);
        }

        /// <summary>Cuántos días atrás mira el job los eventos sin lista (por si un día no corre).</summary>
        internal const int DIAS_ATRAS_SIN_LISTA = 7;

        /// <summary>
        /// Eventos ya pasados (de ayer hacia atrás) en los que nadie ha pasado lista: todas las pagadas se dan por asistidas
        /// (no se bloquea a los clientes) y se apunta la fecha de la lista. Devuelve cada evento con las que se han marcado,
        /// para avisar a su tienda. Los eventos sin nadie pagado no se tocan.
        /// </summary>
        internal async Task<List<KeyValuePair<EventoDTO, List<InscripcionEventoDTO>>>> DarPorAsistidosSinLista()
        {
            DateTime ahora = reloj();
            DateTime hoy = ahora.Date;
            DateTime desde = hoy.AddDays(-DIAS_ATRAS_SIN_LISTA);
            List<Evento> eventos = await db.Eventos
                .Where(e => e.Fecha < hoy && e.Fecha >= desde && e.FechaListaAsistencia == null)
                .ToListAsync().ConfigureAwait(false);
            var resultado = new List<KeyValuePair<EventoDTO, List<InscripcionEventoDTO>>>();
            if (!eventos.Any())
            {
                return resultado;
            }
            List<int> ids = eventos.Select(e => e.Id).ToList();
            List<string> pagadas = new List<string> { EstadoInscripcionEvento.Pagada.ToString(), EstadoInscripcionEvento.Facturada.ToString() };
            List<EventoInscripcion> inscripciones = await db.EventosInscripciones
                .Where(i => ids.Contains(i.EventoId) && pagadas.Contains(i.Estado)).ToListAsync().ConfigureAwait(false);

            foreach (Evento evento in eventos.Where(e => e.Fecha.Date < hoy).OrderBy(e => e.Fecha))
            {
                List<EventoInscripcion> suyas = inscripciones.Where(i => i.EventoId == evento.Id).ToList();
                if (!suyas.Any())
                {
                    continue;
                }
                foreach (EventoInscripcion inscripcion in suyas)
                {
                    Cambiar(inscripcion, EstadoInscripcionEvento.Asistio, USUARIO_AUTOMATICO);
                }
                evento.FechaListaAsistencia = ahora;
                resultado.Add(new KeyValuePair<EventoDTO, List<InscripcionEventoDTO>>(
                    ServicioEventos.ADTO(evento, suyas.Count),
                    await Completar(suyas, new Dictionary<int, Evento> { [evento.Id] = evento }).ConfigureAwait(false)));
            }
            if (resultado.Any())
            {
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            return resultado;
        }

        // ---------------- Ayudantes ----------------

        private void ComprobarPuedeMantener(IPrincipal usuario, Evento evento, bool paraMarcarPagada = false)
        {
            string almacenUsuario = null;
            if (!PermisosEventos.EsDireccion(usuario))
            {
                string sinDominio = PermisosEventos.SinDominio(usuario?.Identity?.Name);
                almacenUsuario = sinDominio == null ? null : almacenDelUsuario(Limpiar(evento.Empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO, sinDominio);
            }
            bool puede = paraMarcarPagada
                ? PermisosEventos.PuedeMarcarPagada(usuario, almacenUsuario, evento.Almacen)
                : PermisosEventos.PuedeMantener(usuario, almacenUsuario, evento.Almacen);
            if (!puede)
            {
                throw Prohibido($"{PermisosEventos.MENSAJE_SIN_PERMISO_EVENTOS} Este evento es de {PermisosEventos.NombreAlmacen(evento.Almacen) ?? "ninguna tienda"}.");
            }
        }

        private void Cambiar(EventoInscripcion inscripcion, EstadoInscripcionEvento nuevo, string usuario)
        {
            EstadoInscripcionEvento actual = EstadoDe(inscripcion);
            if (!ReglasInscripcionEvento.PuedePasar(actual, nuevo))
            {
                throw new NestoBusinessException(
                    $"La inscripción {inscripcion.Id} no puede pasar de {ReglasInscripcionEvento.Texto(actual)} a {ReglasInscripcionEvento.Texto(nuevo)}.");
            }
            inscripcion.Estado = nuevo.ToString();
            inscripcion.Usuario = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            inscripcion.FechaModificacion = reloj();
        }

        private static EstadoInscripcionEvento EstadoDe(EventoInscripcion inscripcion) =>
            ReglasInscripcionEvento.Estado(inscripcion.Estado) ?? EstadoInscripcionEvento.Anulada;

        private static bool OcupaPlaza(EventoInscripcion inscripcion) =>
            ReglasInscripcionEvento.OCUPAN_PLAZA.Contains(EstadoDe(inscripcion));

        private static int OrdenEstado(EstadoInscripcionEvento estado) => (int)estado;

        private static string UsuarioDe(IPrincipal usuario) =>
            UsuarioAuditoriaHelper.ParaAuditoria(UsuarioAuditoriaHelper.Resolver(usuario, null));

        private async Task<Evento> LeerEvento(int id) =>
            await db.Eventos.SingleOrDefaultAsync(e => e.Id == id).ConfigureAwait(false)
                ?? throw NoEncontrado($"No existe el evento {id}.");

        private async Task<EventoInscripcion> LeerInscripcion(int id) =>
            await db.EventosInscripciones.SingleOrDefaultAsync(i => i.Id == id).ConfigureAwait(false)
                ?? throw NoEncontrado($"No existe la inscripción {id}.");

        /// <summary>La ficha del contacto pedido o, si no se pide, la del contacto principal (en memoria: los char vienen con relleno).</summary>
        private async Task<Cliente> LeerFichaCliente(string empresa, string cliente, string contacto)
        {
            List<Cliente> fichas = await db.Clientes.Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente)
                .ToListAsync().ConfigureAwait(false);
            Cliente ficha = contacto != null
                ? fichas.FirstOrDefault(c => Limpiar(c.Contacto) == contacto)
                : fichas.FirstOrDefault(c => c.ClientePrincipal) ?? fichas.FirstOrDefault();
            return ficha ?? throw NoEncontrado(contacto == null
                ? $"No existe el cliente {cliente}."
                : $"No existe el cliente {cliente}/{contacto}.");
        }

        /// <summary>Junta cada inscripción con su evento, el nombre del cliente y, si se puede pagar, la página del enlace.</summary>
        private async Task<List<InscripcionEventoDTO>> Completar(List<EventoInscripcion> inscripciones, Dictionary<int, Evento> eventosConocidos)
        {
            if (!inscripciones.Any())
            {
                return new List<InscripcionEventoDTO>();
            }
            Dictionary<int, Evento> eventos = eventosConocidos ?? new Dictionary<int, Evento>();
            List<int> faltan = inscripciones.Select(i => i.EventoId).Distinct().Where(id => !eventos.ContainsKey(id)).ToList();
            if (faltan.Any())
            {
                foreach (Evento e in await db.Eventos.Where(e => faltan.Contains(e.Id)).ToListAsync().ConfigureAwait(false))
                {
                    eventos[e.Id] = e;
                }
            }

            List<string> empresas = inscripciones.Select(i => Limpiar(i.Empresa)).Distinct().ToList();
            List<string> clientes = inscripciones.Select(i => Limpiar(i.Cliente)).Distinct().ToList();
            var nombres = (await db.Clientes
                .Where(c => empresas.Contains(c.Empresa) && clientes.Contains(c.Nº_Cliente))
                .Select(c => new { c.Nº_Cliente, c.Contacto, c.Nombre })
                .ToListAsync().ConfigureAwait(false))
                .GroupBy(c => Limpiar(c.Nº_Cliente) + "/" + Limpiar(c.Contacto))
                .ToDictionary(g => g.Key, g => g.First().Nombre?.Trim());

            List<int> pagosPendientes = inscripciones
                .Where(i => i.PagoTPVId.HasValue && (EstadoDe(i) == EstadoInscripcionEvento.Reservada || EstadoDe(i) == EstadoInscripcionEvento.Caducada))
                .Select(i => i.PagoTPVId.Value).Distinct().ToList();
            Dictionary<int, Guid> tokens = new Dictionary<int, Guid>();
            if (pagosPendientes.Any())
            {
                tokens = (await db.PagosTPV.Where(p => pagosPendientes.Contains(p.Id))
                    .Select(p => new { p.Id, p.TokenAcceso }).ToListAsync().ConfigureAwait(false))
                    .ToDictionary(p => p.Id, p => p.TokenAcceso);
            }

            Dictionary<int, FacturacionCursoInscripcion> facturaciones = await LeerFacturaciones(inscripciones).ConfigureAwait(false);

            return inscripciones.Select(i =>
            {
                eventos.TryGetValue(i.EventoId, out Evento evento);
                nombres.TryGetValue(Limpiar(i.Cliente) + "/" + Limpiar(i.Contacto), out string nombre);
                InscripcionEventoDTO dto = Componer(i, evento, nombre);
                if (i.PagoTPVId.HasValue && tokens.TryGetValue(i.PagoTPVId.Value, out Guid token))
                {
                    dto.UrlPago = URL_PAGINA_PAGO + token;
                }
                if (facturaciones.TryGetValue(i.Id, out FacturacionCursoInscripcion f))
                {
                    dto.Facturacion = FacturacionCursoDTO.De(f);
                }
                return dto;
            }).ToList();
        }

        /// <summary>NestoAPI#612: la facturación del curso de las que ya están pagadas. Un fallo aquí no rompe la lista.</summary>
        private async Task<Dictionary<int, FacturacionCursoInscripcion>> LeerFacturaciones(List<EventoInscripcion> inscripciones)
        {
            List<int> pagadas = inscripciones.Where(i => ReglasInscripcionEvento.EN_LISTA_ASISTENCIA.Contains(EstadoDe(i))).Select(i => i.Id).ToList();
            if (facturacion == null || !pagadas.Any())
            {
                return new Dictionary<int, FacturacionCursoInscripcion>();
            }
            try
            {
                return await facturacion.Leer(pagadas).ConfigureAwait(false) ?? new Dictionary<int, FacturacionCursoInscripcion>();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cursos #612] No se ha podido leer la facturación de los cursos: {ex.Message}", ex));
                return new Dictionary<int, FacturacionCursoInscripcion>();
            }
        }

        internal static InscripcionEventoDTO Componer(EventoInscripcion i, Evento evento, string nombre)
        {
            EstadoInscripcionEvento estado = EstadoDe(i);
            return new InscripcionEventoDTO
            {
                Id = i.Id,
                EventoId = i.EventoId,
                Evento = evento?.Titulo?.Trim(),
                FechaEvento = evento?.Fecha.Date ?? DateTime.MinValue.Date,
                Almacen = Limpiar(evento?.Almacen),
                Empresa = Limpiar(i.Empresa),
                Cliente = Limpiar(i.Cliente),
                Contacto = Limpiar(i.Contacto),
                Nombre = nombre,
                Origen = ReglasInscripcionEvento.Origen(i.Origen) ?? OrigenInscripcionEvento.Nesto,
                Estado = estado,
                EstadoTexto = ReglasInscripcionEvento.Texto(estado),
                FechaLimitePago = i.FechaLimitePago,
                FormaPago = Limpiar(i.FormaPago),
                ImportePagado = i.ImportePagado,
                FechaPago = i.FechaPago,
                UsuarioPago = Limpiar(i.UsuarioPago),
                PagoTPVId = i.PagoTPVId,
                EventoSenalId = i.EventoSenalId,
                UsuarioCreacion = Limpiar(i.UsuarioCreacion),
                FechaCreacion = i.FechaCreacion
            };
        }

        private static NestoBusinessException NoEncontrado(string mensaje) =>
            new NestoBusinessException(mensaje) { StatusCode = HttpStatusCode.NotFound };

        private static NestoBusinessException Prohibido(string mensaje) =>
            new NestoBusinessException(mensaje) { StatusCode = HttpStatusCode.Forbidden };

        private static string EmpresaOPorDefecto(string empresa) => Limpiar(empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;

        private static string Limpiar(string valor)
        {
            string limpio = valor?.Trim();
            return string.IsNullOrEmpty(limpio) ? null : limpio;
        }
    }

    /// <summary>
    /// NestoAPI#611: lo que llama ServicioPagos al confirmarse el cobro de un enlace de inscripción (Documento «INS…»). Abre
    /// su propio contexto, como el resto de ProcesarNotificacion.
    /// </summary>
    public static class CobroInscripcionEvento
    {
        public static bool EsEnlaceDeInscripcion(PagoTPV pago) =>
            pago != null
            && string.Equals(pago.Tipo?.Trim(), Constantes.TiposPagoTPV.TPV_VIRTUAL, StringComparison.OrdinalIgnoreCase)
            && (pago.Documento?.Trim().StartsWith(ServicioInscripcionesEventos.PREFIJO_DOCUMENTO_PAGO, StringComparison.OrdinalIgnoreCase) ?? false);

        public static async Task Registrar(int pagoRaizId, decimal importe)
        {
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                await new ServicioInscripcionesEventos(db).RegistrarCobroEnlace(pagoRaizId, importe).ConfigureAwait(false);
            }
        }
    }
}
