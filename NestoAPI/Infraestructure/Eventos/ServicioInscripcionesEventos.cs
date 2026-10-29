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
        /// <summary>
        /// La referencia del curso comprado. Carlos (22/10/26): EVENTO{Id:00000} es la plaza reembolsable de ese evento y
        /// EVENTO{Id:00000}NR la no reembolsable; cualquier otra es un producto forzado (Evento.ProductoCurso, puede ser de varios
        /// eventos) y la modalidad sale de lo pagado.
        /// </summary>
        public string Producto { get; set; }
        /// <summary>
        /// Opcional: el evento, cuando Nesto lo sabe. Con un producto forzado compartido por varios eventos, decide cuál; si no
        /// viene, el de la inscripción pendiente del cliente o, si no tiene, el más cercano.
        /// </summary>
        public int? EventoId { get; set; }
        /// <summary>
        /// Opcional (Carlos, 22/10/26): la referencia de la combinación comprada. En la tienda, un curso con dos modalidades es un
        /// producto con dos combinaciones, «Con cupón de productos» (la referencia del producto de Nesto) y «Sin cupón» (la misma
        /// con NR). Si viene, manda sobre Producto para saber la modalidad.
        /// </summary>
        public string Combinacion { get; set; }
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
        /// <summary>
        /// NestoAPI#690: Reembolsable, NoReembolsable o Gratuita. Opcional: si no viene, Gratuita en un evento gratuito y, si
        /// no, Reembolsable (o NoReembolsable si el evento solo tiene esa). 400 si el evento no la ofrece.
        /// </summary>
        public string Modalidad { get; set; }
        /// <summary>
        /// NestoAPI#731: opcional, la persona que asiste (PersonasContactoClientes.Número de una persona del mismo cliente y
        /// contacto; GET api/Eventos/PersonasContacto). 400 si no es de esa ficha. Si no viene y se inscribe la propia clienta
        /// desde la tienda online, la persona cuyo correo es el de su usuario (si la hay).
        /// </summary>
        public string PersonaContacto { get; set; }
    }

    /// <summary>NestoAPI#690: POST api/Eventos/Inscripciones/{id}/ConfirmarAsistencia.</summary>
    public class ConfirmarAsistenciaDTO
    {
        /// <summary>Confirma («Confirmo asistencia») o NoPodraIr («No podré ir»).</summary>
        public string Respuesta { get; set; }
    }

    /// <summary>NestoAPI#690: la respuesta del cliente al recordatorio (o la que apunta la tienda). Null = sin contestar.</summary>
    public class AsistenciaConfirmadaDTO
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public RespuestaAsistenciaEvento Respuesta { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
        /// <summary>Cliente (desde la app o el enlace del correo) o Tienda.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public OrigenRespuestaAsistenciaEvento Origen { get; set; }
    }

    /// <summary>NestoAPI#690: lo que contesta el enlace del correo (para pintar la página).</summary>
    public class ResultadoAsistenciaEnlace
    {
        /// <summary>False si la clave no es de ninguna inscripción (404).</summary>
        public bool Existe { get; set; }
        /// <summary>Se ha apuntado la respuesta.</summary>
        public bool Apuntada { get; set; }
        public string Evento { get; set; }
        public DateTime FechaEvento { get; set; }
        public string HoraInicio { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>Carlos (22/10/26): una reserva sin pagar a la que hay que avisar de que su plaza se libera.</summary>
    public class AvisoPagoInscripcion
    {
        public Evento Evento { get; set; }
        public EventoInscripcion Inscripcion { get; set; }
        /// <summary>El último día para pagar.</summary>
        public DateTime FechaLimite { get; set; }
        /// <summary>El día en que se libera la plaza si no se paga.</summary>
        public DateTime FechaLiberacion { get; set; }
        /// <summary>La página del enlace de pago, si lo tiene.</summary>
        public string UrlPago { get; set; }
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
        /// <summary>
        /// La página del enlace de pago (https://api.nuevavision.es/pago/{token}), al crear y al leer, solo mientras se puede pagar:
        /// inscripción Reservada o Caducada con su PagoTPV pendiente (ni autorizado, ni anulado, ni devuelto). Si no, null.
        /// </summary>
        public string UrlPago { get; set; }
        /// <summary>La señal (#591) cuando el dinero entra por transferencia.</summary>
        public int? EventoSenalId { get; set; }
        public string UsuarioCreacion { get; set; }
        public DateTime FechaCreacion { get; set; }
        /// <summary>NestoAPI#612: la facturación del curso (pedido, factura, liquidación). Null si no ha empezado.</summary>
        public FacturacionCursoDTO Facturacion { get; set; }

        /// <summary>NestoAPI#690: Reembolsable, NoReembolsable o Gratuita (las de antes, Reembolsable).</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public ModalidadInscripcionEvento Modalidad { get; set; }
        /// <summary>NestoAPI#690: lo que se cobra por esta inscripción según su modalidad (0 si es gratuita).</summary>
        public decimal ImporteSenal { get; set; }
        /// <summary>NestoAPI#690: al confirmar el pago se emite cupón (solo Reembolsable).</summary>
        public bool ConCupon { get; set; }
        /// <summary>NestoAPI#690: «Confirmo asistencia» / «No podré ir», cuándo y quién. Null = todavía no ha contestado.</summary>
        public AsistenciaConfirmadaDTO AsistenciaConfirmada { get; set; }
        /// <summary>NestoAPI#690: Pendiente, Confirmada, NoPodraIr, Asistio o NoAsistio (la lista pasada manda).</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public EstadoAsistenciaEvento EstadoAsistencia { get; set; }
        /// <summary>NestoAPI#690: «Pendiente», «Confirmada», «No podrá ir», «Asistió», «No asistió».</summary>
        public string EstadoAsistenciaTexto { get; set; }
        /// <summary>NestoAPI#690: cuándo salió el recordatorio (null = todavía no).</summary>
        public DateTime? RecordatorioEnviado { get; set; }
        /// <summary>
        /// NestoAPI#690: la base imponible de los pedidos del cliente del día del evento en Reina o Alcobendas (sin
        /// presupuestos ni la línea del curso). Solo en la lista de inscritos del evento; null en el resto.
        /// </summary>
        public decimal? BaseImponibleDia { get; set; }
        /// <summary>
        /// Sugerencia 776: avisos (no errores) al inscribir desde la casa, p. ej. que a la ficha le falta el teléfono o el
        /// correo. Solo en la respuesta de POST api/Eventos/{id}/Inscripciones (vacía si no falta nada); null en el resto y
        /// cuando se inscribe la propia clienta desde la tienda online.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Avisos { get; set; }

        /// <summary>
        /// NestoAPI#731: la persona que asiste (una persona de contacto del cliente, de la misma dirección). Null = sin indicar.
        /// Se pone al inscribir (InscribirEventoDTO.PersonaContacto) y se cambia con PUT api/Eventos/Inscripciones/{id}/PersonaContacto.
        /// </summary>
        public PersonaContactoEventoDTO PersonaContacto { get; set; }
        /// <summary>
        /// NestoAPI#731: solo lectura, de la ficha (no se guarda): el correo de la persona que asiste o, si no tiene, el de otra
        /// persona de contacto de la dirección o del cliente. Null si no hay ninguno.
        /// </summary>
        public string Correo { get; set; }
        /// <summary>NestoAPI#731: solo lectura, de la ficha del contacto (no se guarda): «Calle, CP Población (Provincia)».</summary>
        public string Direccion { get; set; }
        /// <summary>
        /// NestoAPI#732: en un evento «gratuito por compra», el pedido de venta que justifica la inscripción (el que tiene el
        /// producto requerido). Null en el resto. Si se borra ese pedido o se le quita la línea, la inscripción se anula.
        /// </summary>
        public int? PedidoJustificante { get; set; }
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
        /// <summary>NestoAPI#690: «Confirmo asistencia» / «No podré ir», desde la app del cliente o por la tienda.</summary>
        Task<InscripcionEventoDTO> ConfirmarAsistencia(int id, ConfirmarAsistenciaDTO peticion, IPrincipal usuario);
        /// <summary>NestoAPI#690: lo que enseña el enlace del correo (sin apuntar nada).</summary>
        Task<ResultadoAsistenciaEnlace> LeerAsistenciaConClave(Guid clave);
        /// <summary>NestoAPI#690: la respuesta que llega por el enlace del correo (sin usuario).</summary>
        Task<ResultadoAsistenciaEnlace> ConfirmarAsistenciaConClave(Guid clave, string respuesta);
        /// <summary>NestoAPI#614: la compra del curso en la tienda PrestaShop paga (o crea) la inscripción del cliente.</summary>
        Task<InscripcionEventoDTO> InscribirDesdeTienda(CursoTiendaDTO peticion, IPrincipal usuario);
        /// <summary>
        /// NestoAPI#691: el enlace de pago único del pedido de cursos de la plantilla, por el total de sus inscripciones. Devuelve la
        /// primera inscripción con UrlPago.
        /// </summary>
        Task<InscripcionEventoDTO> GenerarEnlacePagoPedidoCursos(int pedido, List<int> inscripciones, IPrincipal usuario);
        /// <summary>NestoAPI#731: las personas de contacto de un cliente (de un contacto o de todos), para el combo «Asiste». Solo la casa.</summary>
        Task<List<PersonaContactoEventoDTO>> LeerPersonasContacto(string empresa, string cliente, string contacto, IPrincipal usuario);
        /// <summary>NestoAPI#731: cambia (o quita) la persona que asiste. Quien mantiene el evento.</summary>
        Task<InscripcionEventoDTO> CambiarPersonaContacto(int id, CambiarPersonaContactoInscripcionDTO peticion, IPrincipal usuario);
    }

    /// <summary>
    /// NestoAPI#690: la base imponible de los pedidos de unos clientes en un día, en Reina o Alcobendas (lista de asistencia).
    /// </summary>
    public interface ILectorBaseImponibleDia
    {
        /// <summary>Cliente (sin relleno) → suma de la BaseImponible. Los que no compraron no salen.</summary>
        Task<Dictionary<string, decimal>> Leer(string empresa, DateTime dia, IReadOnlyCollection<string> clientes, string productoExcluido);
    }

    /// <summary>
    /// NestoAPI#690: una consulta ligera, con NOLOCK y por los clientes inscritos (índice por Nº Cliente de CabPedidoVta): la
    /// cabecera con fecha del día, sus líneas en REI o ALC, sin presupuestos y sin la línea del propio curso.
    /// </summary>
    public class LectorBaseImponibleDia : ILectorBaseImponibleDia
    {
        internal const string SQL = @"
SELECT RTRIM(c.[Nº Cliente]) AS Cliente, SUM(l.[Base Imponible]) AS BaseImponible
FROM dbo.CabPedidoVta c WITH (NOLOCK)
JOIN dbo.LinPedidoVta l WITH (NOLOCK) ON l.Empresa = c.Empresa AND l.Número = c.Número
WHERE c.Empresa = @empresa AND c.[Nº Cliente] IN ({0}) AND c.Fecha >= @dia AND c.Fecha < DATEADD(day, 1, @dia)
  AND l.Almacén IN ('REI', 'ALC') AND l.Estado >= -1
  AND (@producto IS NULL OR l.Producto IS NULL OR RTRIM(l.Producto) <> @producto)
GROUP BY RTRIM(c.[Nº Cliente])";

        private readonly NVEntities db;

        public LectorBaseImponibleDia(NVEntities db)
        {
            this.db = db;
        }

        public async Task<Dictionary<string, decimal>> Leer(string empresa, DateTime dia, IReadOnlyCollection<string> clientes, string productoExcluido)
        {
            List<string> lista = (clientes ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!lista.Any())
            {
                return new Dictionary<string, decimal>();
            }
            var parametros = new List<object>
            {
                new System.Data.SqlClient.SqlParameter("@empresa", empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO),
                new System.Data.SqlClient.SqlParameter("@dia", System.Data.SqlDbType.DateTime) { Value = dia.Date },
                new System.Data.SqlClient.SqlParameter("@producto", (object)productoExcluido?.Trim() ?? DBNull.Value)
            };
            var nombres = new List<string>();
            for (int i = 0; i < lista.Count; i++)
            {
                nombres.Add("@c" + i);
                parametros.Add(new System.Data.SqlClient.SqlParameter("@c" + i, lista[i]));
            }
            List<BaseImponibleCliente> filas = await db.Database
                .SqlQuery<BaseImponibleCliente>(string.Format(SQL, string.Join(", ", nombres)), parametros.ToArray())
                .ToListAsync().ConfigureAwait(false);
            return filas.Where(f => f.Cliente != null).GroupBy(f => f.Cliente.Trim())
                .ToDictionary(g => g.Key, g => g.Sum(f => f.BaseImponible ?? 0));
        }

        private class BaseImponibleCliente
        {
            public string Cliente { get; set; }
            public decimal? BaseImponible { get; set; }
        }
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
        /// <summary>NestoAPI#690: quién consta cuando el cliente contesta por el enlace del correo (no hay usuario).</summary>
        public const string USUARIO_ENLACE_CORREO = "Enlace del correo";
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
        private ILectorBaseImponibleDia baseImponibleDia;
        private readonly Func<bool> cursosTiendaEncendido;
        private readonly Func<bool> liberarReservasEncendido;

        /// <summary>NestoAPI#612: en producción, el enganche es el que factura el curso (detrás de su interruptor).</summary>
        public ServicioInscripcionesEventos(NVEntities db)
            : this(db, () => DateTime.Now,
                () => new ServicioPagos(new RedsysService(), new ContabilidadService(), new LectorParametrosUsuario()),
                null, new ConfirmadorSenalEvento(db), new RepositorioFacturacionCursos(db),
                (asistencias, usuario) => new Cupones.ServicioCupones(db).AplicarAsistencia(asistencias, usuario))
        {
            baseImponibleDia = new LectorBaseImponibleDia(db);
            PersonasAsistentes = new RepositorioPersonaAsistenteEventos(db);
            ProductosRequeridos = new RepositorioProductosRequeridosEventos(db);
        }

        /// <summary>
        /// NestoAPI#741: el servicio de «Inscribir» a petición de un usuario (POST api/Eventos/{id}/Inscripciones): cada comercial
        /// solo a sus clientes (<see cref="AutorizadorInscripcion"/>). Los demás flujos que inscriben (señal, plantilla,
        /// PrestaShop, tienda, jobs) usan el constructor normal y no se acotan.
        /// </summary>
        public static ServicioInscripcionesEventos ParaPeticionDeUsuario(NVEntities db) =>
            new ServicioInscripcionesEventos(db)
            {
                AutorizadorInscripcion = new AutorizadorInscripcionEventos(new LectorVendedoresInscripcion(db))
            };

        /// <summary>
        /// NestoAPI#741: quién puede inscribir a quién (<see cref="PermisosEventos.PuedeInscribir"/>). Null (los flujos
        /// automáticos y los tests): no se acota a los usuarios de la casa.
        /// </summary>
        internal AutorizadorInscripcionEventos AutorizadorInscripcion { get; set; }

        /// <summary>
        /// NestoAPI#732: los productos requeridos de los eventos «gratuitos por compra» y el pedido que justifica cada inscripción
        /// (fuera del EDMX). Null (tests): ningún evento exige compra.
        /// </summary>
        internal IRepositorioProductosRequeridosEventos ProductosRequeridos { get; set; }

        /// <summary>NestoAPI#690: la base imponible del día en la lista de inscritos. Null (tests): no se calcula.</summary>
        internal ILectorBaseImponibleDia BaseImponibleDia { get => baseImponibleDia; set => baseImponibleDia = value; }

        /// <summary>
        /// NestoAPI#731: la columna EventosInscripciones.PersonaContacto (fuera del EDMX). Null (tests): la persona que asiste ni
        /// se lee ni se guarda, y el PUT para cambiarla contesta que falta el script.
        /// </summary>
        internal IRepositorioPersonaAsistenteEventos PersonasAsistentes { get; set; }

        /// <param name="pagos">Para generar el enlace (perezoso: solo se crea si hace falta).</param>
        /// <param name="almacenDelUsuario">(empresa, usuario sin dominio) → su AlmacénPedidoVta. Null: el de ParametrosUsuario.</param>
        /// <param name="procesador">El enganche del corte 2. Null: <see cref="ProcesadorInscripcionPagadaPendiente"/>.</param>
        /// <param name="facturacion">NestoAPI#612: para enseñar en qué punto está la facturación del curso. Null: no se enseña.</param>
        /// <param name="aplicarAsistenciaCupones">NestoAPI#613: (inscripción → ¿vino?, usuario) anula o recupera su cupón. Null: nada.</param>
        internal ServicioInscripcionesEventos(NVEntities db, Func<DateTime> reloj, Func<IServicioPagos> pagos,
            Func<string, string, string> almacenDelUsuario = null, IProcesadorInscripcionPagada procesador = null,
            IRepositorioFacturacionCursos facturacion = null, Func<List<KeyValuePair<int, bool>>, string, Task> aplicarAsistenciaCupones = null,
            Func<bool> cursosTiendaEncendido = null, Func<bool> liberarReservasEncendido = null)
        {
            this.liberarReservasEncendido = liberarReservasEncendido ?? (() => InterruptoresEventos.LiberarReservasSinPagar);
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
                    throw Prohibido("Solo puede inscribirse a sí mismo.");
                }
                origen = OrigenInscripcionEvento.TNV;
            }
            else
            {
                OrigenInscripcionEvento? leido = ReglasInscripcionEvento.Origen(peticion.Origen);
                if (!leido.HasValue || !origenesDeLaCasa.Contains(leido.Value))
                {
                    throw new NestoBusinessException("Indique desde dónde se inscribe: Nesto, NestoApp o Teléfono.");
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

            // NestoAPI#741: los de la casa, cada comercial a sus clientes (el jefe, a los de su equipo); 403 si no.
            if (clienteDelToken == null && AutorizadorInscripcion != null)
            {
                await AutorizadorInscripcion.Comprobar(usuario, empresa, cliente, contacto).ConfigureAwait(false);
            }

            List<EventoInscripcion> delEvento = await db.EventosInscripciones
                .Where(i => i.EventoId == evento.Id).ToListAsync().ConfigureAwait(false);
            List<EventoInscripcion> ocupan = delEvento.Where(OcupaPlaza).ToList();
            // Ya inscrito: cualquier inscripción viva, también la de quien dijo «No podré ir» (libera la plaza, pero para
            // volver basta con confirmar la asistencia: no se crea otra).
            EventoInscripcion yaInscrita = delEvento.FirstOrDefault(i => ReglasInscripcionEvento.OCUPAN_PLAZA.Contains(EstadoDe(i))
                && Limpiar(i.Cliente) == cliente && Limpiar(i.Contacto) == contacto);
            if (yaInscrita != null)
            {
                throw new NestoBusinessException(OcupaPlaza(yaInscrita)
                    ? $"El cliente {cliente}/{contacto} ya está inscrito en «{evento.Titulo?.Trim()}»."
                    : $"El cliente {cliente}/{contacto} ya está inscrito en «{evento.Titulo?.Trim()}», pero dijo que no podría ir: " +
                      "si al final viene, confirme su asistencia en esa inscripción.");
            }
            if (evento.Plazas.HasValue && ocupan.Count >= evento.Plazas.Value)
            {
                throw new NestoBusinessException($"No quedan plazas en «{evento.Titulo?.Trim()}» ({evento.Plazas.Value} ocupadas).");
            }

            // NestoAPI#731: la persona que asiste (opcional). Si la dicen, tiene que ser de la ficha.
            List<KeyValuePair<string, PersonaContactoEventoDTO>> personasCliente = null;
            string personaAsiste = null;
            string personaPedida = Limpiar(peticion.PersonaContacto);
            if (personaPedida != null)
            {
                personasCliente = await LeerPersonasDe(new List<string> { empresa }, new List<string> { cliente }).ConfigureAwait(false);
                personaAsiste = ComprobarPersonaDeLaFicha(personasCliente, cliente, contacto, personaPedida);
            }
            else if (origen == OrigenInscripcionEvento.TNV)
            {
                personasCliente = await LeerPersonasSinFallar(empresa, cliente).ConfigureAwait(false);
                personaAsiste = PersonaDelCorreo(personasCliente, cliente, contacto, usuario?.Identity?.Name);
            }

            // NestoAPI#732: «gratuito por compra». Si el evento exige productos, el cliente tiene que haber comprado o pedido
            // alguno (modo «Alguno») o todos (modo «Todos»; ReglasProductosRequeridosEvento); el pedido que lo justifica se
            // guarda en la inscripción.
            JustificanteCompraEvento justificante = null;
            if (ProductosRequeridos != null)
            {
                List<ProductoRequeridoLeido> requeridos = (await ProductosRequeridos.Leer(new List<int> { evento.Id }).ConfigureAwait(false))?
                    .FirstOrDefault().Value ?? new List<ProductoRequeridoLeido>();
                if (requeridos.Any())
                {
                    DateTime desde = ReglasProductosRequeridosEvento.Desde(requeridos);
                    bool exigeTodos = (await ProductosRequeridos.EventosQueExigenTodos(new List<int> { evento.Id }).ConfigureAwait(false))?
                        .Contains(evento.Id) == true;
                    justificante = exigeTodos
                        ? await JustificanteDeTodos(empresa, cliente, evento.Titulo, requeridos, desde).ConfigureAwait(false)
                        : await ProductosRequeridos.BuscarJustificante(empresa, cliente,
                            requeridos.Select(r => r.Producto).ToList(), desde).ConfigureAwait(false)
                            ?? throw ReglasProductosRequeridosEvento.RequiereCompra(evento.Titulo, requeridos, desde);
                }
            }

            // NestoAPI#690: la modalidad fija lo que se cobra y si habrá cupón. La gratuita no se paga: entra Inscrita, sin
            // fecha límite ni forma de pago, y va directa a la lista de asistencia.
            ModalidadInscripcionEvento modalidad = ReglasModalidadEvento.Elegir(evento, peticion.Modalidad);
            bool gratuita = modalidad == ModalidadInscripcionEvento.Gratuita;
            decimal importe = ReglasModalidadEvento.Importe(evento, modalidad);

            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(UsuarioAuditoriaHelper.Resolver(usuario, null));
            var inscripcion = new EventoInscripcion
            {
                EventoId = evento.Id,
                Empresa = empresa,
                Cliente = cliente,
                Contacto = contacto,
                Origen = origen.ToString(),
                Estado = (gratuita ? EstadoInscripcionEvento.Inscrita : EstadoInscripcionEvento.Reservada).ToString(),
                // Carlos (22/10/26): con Eventos:LiberarReservasSinPagar, el plazo del evento (DiasPagoAntesEvento)
                FechaLimitePago = gratuita ? (DateTime?)null : liberarReservasEncendido()
                    ? ReglasInscripcionEvento.FechaLimitePago(ahora, evento)
                    : ReglasInscripcionEvento.FechaLimitePago(ahora, evento.Fecha),
                FormaPago = gratuita ? null : formaPago?.ToString(),
                Modalidad = modalidad.ToString(),
                UsuarioCreacion = usuarioAuditoria,
                FechaCreacion = ahora,
                Usuario = usuarioAuditoria,
                FechaModificacion = ahora
            };
            db.EventosInscripciones.Add(inscripcion);
            await db.SaveChangesAsync().ConfigureAwait(false);
            bool personaGuardada = await GuardarPersonaSinFallar(inscripcion.Id, personaAsiste).ConfigureAwait(false);
            bool justificanteGuardado = await GuardarJustificanteSinFallar(inscripcion.Id, justificante?.Pedido).ConfigureAwait(false);

            string urlPago = null;
            bool conEnlace = !gratuita && peticion.GenerarEnlacePago && importe > 0
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
                        Importe = importe,
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
            // NestoAPI#731: lo de la ficha (solo lectura) y la persona que asiste
            personasCliente = personasCliente ?? await LeerPersonasSinFallar(empresa, cliente).ConfigureAwait(false);
            RellenarFicha(dto, ficha.Dirección, ficha.CodPostal, ficha.Población, ficha.Provincia,
                personasCliente.Where(p => p.Key == cliente).Select(p => p.Value), personaGuardada ? personaAsiste : null);
            dto.PedidoJustificante = justificanteGuardado ? justificante?.Pedido : null;
            if (origen != OrigenInscripcionEvento.TNV)
            {
                dto.Avisos = await AvisosFicha(empresa, cliente, contacto, ficha.Teléfono).ConfigureAwait(false);
            }
            return dto;
        }

        /// <summary>
        /// Sugerencia 776: si a la ficha le falta el teléfono o el correo (<see cref="AvisosFichaInscripcion"/>). Nunca
        /// bloquea: si algo falla al mirarlo, sin avisos (y a ELMAH).
        /// </summary>
        private async Task<List<string>> AvisosFicha(string empresa, string cliente, string contacto, string telefonoFicha)
        {
            try
            {
                List<AvisosFichaInscripcion.PersonaContacto> personas = await db.PersonasContactoClientes
                    .Where(p => p.Empresa == empresa && p.NºCliente == cliente)
                    .Select(p => new AvisosFichaInscripcion.PersonaContacto
                    {
                        Contacto = p.Contacto,
                        Telefono = p.Teléfono,
                        Correo = p.CorreoElectrónico
                    })
                    .ToListAsync().ConfigureAwait(false);
                return AvisosFichaInscripcion.Calcular(contacto, telefonoFicha, personas);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Sugerencia 776] No se ha podido mirar el teléfono y el correo de {cliente}/{contacto}: {ex.Message}", ex));
                return new List<string>();
            }
        }

        // ---------------- NestoAPI#731: persona que asiste, correo y dirección ----------------

        public async Task<List<PersonaContactoEventoDTO>> LeerPersonasContacto(string empresa, string cliente, string contacto, IPrincipal usuario)
        {
            if (PermisosEventos.ClienteDelToken(usuario) != null)
            {
                throw Prohibido("Las personas de contacto del cliente solo las ve la casa.");
            }
            string clienteLimpio = Limpiar(cliente) ?? throw new NestoBusinessException("Indique el cliente.");
            string contactoLimpio = Limpiar(contacto);
            List<KeyValuePair<string, PersonaContactoEventoDTO>> personas =
                await LeerPersonasDe(new List<string> { EmpresaOPorDefecto(empresa) }, new List<string> { clienteLimpio }).ConfigureAwait(false);
            return personas.Where(p => p.Key == clienteLimpio && (contactoLimpio == null || p.Value.Contacto == contactoLimpio))
                .Select(p => p.Value)
                .OrderBy(p => p.Contacto).ThenBy(p => p.Numero)
                .ToList();
        }

        public async Task<InscripcionEventoDTO> CambiarPersonaContacto(int id, CambiarPersonaContactoInscripcionDTO peticion, IPrincipal usuario)
        {
            EventoInscripcion inscripcion = await LeerInscripcion(id).ConfigureAwait(false);
            Evento evento = await LeerEvento(inscripcion.EventoId).ConfigureAwait(false);
            ComprobarPuedeMantener(usuario, evento);
            string numero = Limpiar(peticion?.PersonaContacto);
            if (numero != null)
            {
                List<KeyValuePair<string, PersonaContactoEventoDTO>> personas = await LeerPersonasDe(
                    new List<string> { EmpresaOPorDefecto(inscripcion.Empresa) }, new List<string> { Limpiar(inscripcion.Cliente) }).ConfigureAwait(false);
                numero = ComprobarPersonaDeLaFicha(personas, Limpiar(inscripcion.Cliente), Limpiar(inscripcion.Contacto), numero);
            }
            if (PersonasAsistentes == null || !await PersonasAsistentes.Poner(inscripcion.Id, numero).ConfigureAwait(false))
            {
                throw new NestoBusinessException("Todavía no se puede indicar quién asiste: falta lanzar el script " +
                    "Scripts/Issue731_EventosInscripcionesPersonaContacto.sql. Avise a Informática.");
            }
            inscripcion.Usuario = UsuarioDe(usuario);
            inscripcion.FechaModificacion = reloj();
            await db.SaveChangesAsync().ConfigureAwait(false);
            return (await Completar(new List<EventoInscripcion> { inscripcion }, new Dictionary<int, Evento> { [evento.Id] = evento }).ConfigureAwait(false))
                .Single();
        }

        /// <summary>Las personas de contacto de unos clientes (cliente → persona), ya limpias.</summary>
        private async Task<List<KeyValuePair<string, PersonaContactoEventoDTO>>> LeerPersonasDe(List<string> empresas, List<string> clientes)
        {
            var filas = await db.PersonasContactoClientes
                .Where(p => empresas.Contains(p.Empresa) && clientes.Contains(p.NºCliente))
                .Select(p => new { p.NºCliente, p.Contacto, p.Número, p.Nombre, p.CorreoElectrónico, p.Teléfono })
                .ToListAsync().ConfigureAwait(false);
            return filas.Select(p => new KeyValuePair<string, PersonaContactoEventoDTO>(Limpiar(p.NºCliente), new PersonaContactoEventoDTO
            {
                Contacto = Limpiar(p.Contacto),
                Numero = Limpiar(p.Número),
                Nombre = Limpiar(p.Nombre),
                Correo = Limpiar(p.CorreoElectrónico),
                Telefono = Limpiar(p.Teléfono)
            })).ToList();
        }

        /// <summary>Lo mismo, para enseñar: si falla, sin personas (y a ELMAH). Nunca bloquea.</summary>
        private async Task<List<KeyValuePair<string, PersonaContactoEventoDTO>>> LeerPersonasSinFallar(string empresa, string cliente)
        {
            try
            {
                return await LeerPersonasDe(new List<string> { empresa }, new List<string> { cliente }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Eventos #731] No se han podido leer las personas de contacto de {cliente}: {ex.Message}", ex));
                return new List<KeyValuePair<string, PersonaContactoEventoDTO>>();
            }
        }

        /// <summary>El Número limpio si la persona es de ese cliente y contacto; 400 si no.</summary>
        internal static string ComprobarPersonaDeLaFicha(IEnumerable<KeyValuePair<string, PersonaContactoEventoDTO>> personas, string cliente,
            string contacto, string numero)
        {
            PersonaContactoEventoDTO persona = (personas ?? Enumerable.Empty<KeyValuePair<string, PersonaContactoEventoDTO>>())
                .Where(p => p.Key == cliente && p.Value.Contacto == contacto)
                .Select(p => p.Value)
                .FirstOrDefault(p => string.Equals(p.Numero, numero?.Trim(), StringComparison.OrdinalIgnoreCase));
            return persona?.Numero ?? throw new NestoBusinessException(
                $"La persona de contacto {numero?.Trim()} no es de la ficha del cliente {cliente}/{contacto}. Elija una de sus personas de contacto.");
        }

        /// <summary>NestoAPI#731: la clienta que se inscribe desde la tienda es la persona de su dirección con el correo de su usuario.</summary>
        internal static string PersonaDelCorreo(IEnumerable<KeyValuePair<string, PersonaContactoEventoDTO>> personas, string cliente, string contacto,
            string correoUsuario)
        {
            string correo = correoUsuario?.Trim();
            if (string.IsNullOrEmpty(correo) || correo.IndexOf('@') < 0)
            {
                return null;
            }
            return (personas ?? Enumerable.Empty<KeyValuePair<string, PersonaContactoEventoDTO>>())
                .Where(p => p.Key == cliente && p.Value.Contacto == contacto && p.Value.Correo != null)
                .Select(p => p.Value)
                .FirstOrDefault(p => p.Correo.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(c => string.Equals(c.Trim(), correo, StringComparison.OrdinalIgnoreCase)))?.Numero;
        }

        /// <summary>Guarda la persona que asiste de una inscripción recién creada. Sin columna o si falla: sin persona (y a ELMAH).</summary>
        private async Task<bool> GuardarPersonaSinFallar(int inscripcion, string numero)
        {
            if (numero == null || PersonasAsistentes == null)
            {
                return false;
            }
            try
            {
                return await PersonasAsistentes.Poner(inscripcion, numero).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Eventos #731] La inscripción {inscripcion} está hecha, pero no se ha podido guardar quién asiste ({numero}): {ex.Message}", ex));
                return false;
            }
        }

        /// <summary>
        /// NestoAPI#732, modo «Todos»: cada producto requerido tiene que tener su línea (mismas líneas válidas y misma ventana
        /// que con «Alguno»). Si falta alguno, 400 EVENTO_REQUIERE_COMPRA con los que faltan; si no, el justificante es el pedido
        /// más reciente de los que justifican.
        /// </summary>
        private async Task<JustificanteCompraEvento> JustificanteDeTodos(string empresa, string cliente, string titulo,
            List<ProductoRequeridoLeido> requeridos, DateTime desde)
        {
            var encontrados = new List<JustificanteCompraEvento>();
            var faltan = new List<ProductoRequeridoLeido>();
            foreach (ProductoRequeridoLeido requerido in requeridos)
            {
                JustificanteCompraEvento suyo = await ProductosRequeridos.BuscarJustificante(empresa, cliente,
                    new List<string> { requerido.Producto }, desde).ConfigureAwait(false);
                if (suyo == null)
                {
                    faltan.Add(requerido);
                }
                else
                {
                    encontrados.Add(suyo);
                }
            }
            if (faltan.Any())
            {
                throw ReglasProductosRequeridosEvento.RequiereCompraTodos(titulo, faltan, desde);
            }
            return encontrados.OrderByDescending(j => j.Pedido).First();
        }

        /// <summary>
        /// NestoAPI#732: el pedido que justifica la inscripción recién creada. Sin columna o si falla: la inscripción vale igual
        /// (la compra está comprobada), pero borrar el pedido no la anulará (y queda en ELMAH).
        /// </summary>
        private async Task<bool> GuardarJustificanteSinFallar(int inscripcion, int? pedido)
        {
            if (!pedido.HasValue || ProductosRequeridos == null)
            {
                return false;
            }
            try
            {
                bool guardado = await ProductosRequeridos.PonerJustificante(inscripcion, pedido.Value).ConfigureAwait(false);
                if (!guardado)
                {
                    ElmahHelper.Log(new Exception($"[Eventos #732] La inscripción {inscripcion} está justificada por el pedido {pedido}, pero falta la " +
                        "columna EventosInscripciones.PedidoJustificante (Scripts/Issue732_EventosProductosRequeridos.sql)."));
                }
                return guardado;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Eventos #732] La inscripción {inscripcion} está hecha, pero no se ha podido guardar su pedido justificante {pedido}: {ex.Message}", ex));
                return false;
            }
        }

        /// <summary>NestoAPI#731: dirección y correo de la ficha (solo lectura) y la persona que asiste.</summary>
        private static void RellenarFicha(InscripcionEventoDTO dto, string direccion, string codigoPostal, string poblacion, string provincia,
            IEnumerable<PersonaContactoEventoDTO> personasDelCliente, string personaAsiste)
        {
            List<PersonaContactoEventoDTO> personas = (personasDelCliente ?? Enumerable.Empty<PersonaContactoEventoDTO>()).ToList();
            dto.Direccion = FichaInscripcionEvento.Direccion(direccion, codigoPostal, poblacion, provincia);
            dto.Correo = FichaInscripcionEvento.Correo(dto.Contacto, personaAsiste, personas);
            if (personaAsiste != null)
            {
                dto.PersonaContacto = personas.FirstOrDefault(p => p.Contacto == dto.Contacto && p.Numero == personaAsiste)
                    // La persona se ha borrado de la ficha: se enseña el número que quedó guardado
                    ?? new PersonaContactoEventoDTO { Contacto = dto.Contacto, Numero = personaAsiste };
            }
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
            await RellenarBaseImponibleDia(lista, evento).ConfigureAwait(false);
            return lista.OrderBy(i => OrdenEstado(i.Estado)).ThenBy(i => i.Nombre).ThenBy(i => i.Cliente).ToList();
        }

        /// <summary>
        /// NestoAPI#690: en la lista de inscritos, la base imponible de los pedidos de cada cliente del día del evento en Reina
        /// o Alcobendas (0 si no compró; null si no se ha podido leer). Una consulta para todos. Un fallo no rompe la lista.
        /// </summary>
        private async Task RellenarBaseImponibleDia(List<InscripcionEventoDTO> lista, Evento evento)
        {
            // Antes del día del evento no hay nada que sumar: ni se consulta (null).
            if (baseImponibleDia == null || lista == null || !lista.Any() || evento.Fecha.Date > reloj().Date)
            {
                return;
            }
            try
            {
                Dictionary<string, decimal> bases = await baseImponibleDia.Leer(EmpresaOPorDefecto(evento.Empresa), evento.Fecha.Date,
                    lista.Select(i => i.Cliente).Distinct().ToList(), Limpiar(evento.ProductoCurso)).ConfigureAwait(false)
                    ?? new Dictionary<string, decimal>();
                var porCliente = new Dictionary<string, decimal>(bases, StringComparer.OrdinalIgnoreCase);
                foreach (InscripcionEventoDTO inscripcion in lista)
                {
                    inscripcion.BaseImponibleDia = inscripcion.Cliente != null && porCliente.TryGetValue(inscripcion.Cliente, out decimal b)
                        ? Math.Round(b, 2, MidpointRounding.AwayFromZero)
                        : 0;
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Eventos #690] No se ha podido leer la base imponible del día del evento {evento.Id}: {ex.Message}", ex));
            }
        }

        public async Task<List<InscripcionEventoDTO>> LeerInscripcionesCliente(string empresa, string cliente, string contacto, IPrincipal usuario)
        {
            string clienteLimpio = Limpiar(cliente) ?? throw new NestoBusinessException("Falta el cliente.");
            string clienteDelToken = PermisosEventos.ClienteDelToken(usuario);
            if (clienteDelToken != null && clienteDelToken != clienteLimpio)
            {
                throw Prohibido("Solo puede ver sus inscripciones.");
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
                throw Prohibido("Solo puede anular sus inscripciones.");
            }
            EstadoInscripcionEvento estado = EstadoDe(inscripcion);
            // NestoAPI#690: la gratuita (Inscrita) se anula igual: no hay dinero de por medio.
            if (estado != EstadoInscripcionEvento.Reservada && estado != EstadoInscripcionEvento.Inscrita)
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
            ComprobarQueNoEsGratuita(inscripcion, evento);

            FormaPagoInscripcionEvento forma = ReglasInscripcionEvento.FormaPago(peticion?.FormaPago)
                ?? throw new NestoBusinessException("Indique cómo ha pagado: Tarjeta, Transferencia o Efectivo.");
            // NestoAPI#690: por defecto, la señal de su modalidad (reembolsable o no reembolsable)
            decimal importe = Math.Round(peticion.Importe ?? ReglasModalidadEvento.ImporteDe(inscripcion, evento), 2, MidpointRounding.AwayFromZero);
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
            List<EventoInscripcion> delEnlace = await db.EventosInscripciones
                .Where(i => i.PagoTPVId == pagoRaizId).ToListAsync().ConfigureAwait(false);
            if (!delEnlace.Any())
            {
                return null;
            }
            if (delEnlace.Count > 1)
            {
                return await RegistrarCobroEnlaceDelPedido(pagoRaizId, delEnlace).ConfigureAwait(false);
            }
            EventoInscripcion inscripcion = delEnlace[0];
            Evento evento = await LeerEvento(inscripcion.EventoId).ConfigureAwait(false);
            EstadoInscripcionEvento estado = EstadoDe(inscripcion);
            if (estado == EstadoInscripcionEvento.Anulada)
            {
                ElmahHelper.Log(new Exception($"[Inscripciones #611] Se ha cobrado el enlace {pagoRaizId} de la inscripción {inscripcion.Id} " +
                    $"(cliente {Limpiar(inscripcion.Cliente)}, evento «{evento.Titulo?.Trim()}»), pero estaba anulada: el cobro está en su " +
                    "extracto; decidan si se reactiva (marcándola pagada) o se devuelve."));
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
        /// NestoAPI#691 (Carlos, 22/10/26): el enlace único del pedido de cursos de la plantilla paga TODAS sus inscripciones, cada
        /// una por el importe de su modalidad. Primero se apuntan todas como pagadas y después se procesan: la primera factura
        /// las líneas de todas (una sola factura) y las demás ya la encuentran hecha.
        /// </summary>
        private async Task<InscripcionEventoDTO> RegistrarCobroEnlaceDelPedido(int pagoRaizId, List<EventoInscripcion> delEnlace)
        {
            DateTime ahora = reloj();
            var pagadas = new List<KeyValuePair<EventoInscripcion, Evento>>();
            foreach (EventoInscripcion inscripcion in delEnlace.OrderBy(i => i.Id))
            {
                Evento evento = await LeerEvento(inscripcion.EventoId).ConfigureAwait(false);
                EstadoInscripcionEvento estado = EstadoDe(inscripcion);
                if (estado == EstadoInscripcionEvento.Anulada)
                {
                    ElmahHelper.Log(new Exception($"[Cursos #691] Se ha cobrado el enlace {pagoRaizId} del pedido de cursos, pero la inscripción " +
                        $"{inscripcion.Id} (cliente {Limpiar(inscripcion.Cliente)}, evento «{evento.Titulo?.Trim()}») estaba anulada: su parte del " +
                        "cobro está en el extracto; decidan si se reactiva (marcándola pagada) o se devuelve."));
                    continue;
                }
                if (!ReglasInscripcionEvento.PuedePasar(estado, EstadoInscripcionEvento.Pagada) || estado == EstadoInscripcionEvento.Pagada)
                {
                    continue;
                }
                AnotarPago(inscripcion, FormaPagoInscripcionEvento.Tarjeta, ReglasModalidadEvento.ImporteDe(inscripcion, evento), ahora, USUARIO_AUTOMATICO, null);
                pagadas.Add(new KeyValuePair<EventoInscripcion, Evento>(inscripcion, evento));
            }
            if (pagadas.Any())
            {
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            foreach (KeyValuePair<EventoInscripcion, Evento> pagada in pagadas)
            {
                await ProcesarPagada(pagada.Key, pagada.Value).ConfigureAwait(false);
            }
            EventoInscripcion primera = delEnlace.OrderBy(i => i.Id).First();
            return Componer(primera, await LeerEvento(primera.EventoId).ConfigureAwait(false), null);
        }

        /// <summary>
        /// NestoAPI#691: el enlace de pago único del pedido de cursos de la plantilla, por el total de sus inscripciones (cada una,
        /// el importe de su modalidad). Es un enlace de pago como el de una inscripción (PagosTPV.Documento «INS{id}» de la primera,
        /// para que el cobro no se proponga como señal y se liquide contra la factura) y, como lleva el pedido en el concepto, es
        /// también un enlace del pedido (#679). Todas las inscripciones apuntan a él (PagoTPVId): al cobrarlo, quedan pagadas todas.
        /// </summary>
        public async Task<InscripcionEventoDTO> GenerarEnlacePagoPedidoCursos(int pedido, List<int> inscripciones, IPrincipal usuario)
        {
            List<int> ids = (inscripciones ?? new List<int>()).Distinct().ToList();
            List<EventoInscripcion> lista = (await db.EventosInscripciones.Where(i => ids.Contains(i.Id)).ToListAsync().ConfigureAwait(false))
                .OrderBy(i => i.Id).ToList();
            if (!lista.Any())
            {
                throw NoEncontrado($"No existen las inscripciones del pedido {pedido}.");
            }
            decimal total = 0;
            var eventos = new Dictionary<int, Evento>();
            foreach (EventoInscripcion inscripcion in lista)
            {
                if (!eventos.TryGetValue(inscripcion.EventoId, out Evento evento))
                {
                    evento = await LeerEvento(inscripcion.EventoId).ConfigureAwait(false);
                    eventos[evento.Id] = evento;
                }
                total += ReglasModalidadEvento.ImporteDe(inscripcion, evento);
            }
            total = Math.Round(total, 2, MidpointRounding.AwayFromZero);
            EventoInscripcion primera = lista[0];
            string empresa = EmpresaOPorDefecto(primera.Empresa);
            Cliente ficha = await LeerFichaCliente(empresa, Limpiar(primera.Cliente), Limpiar(primera.Contacto)).ConfigureAwait(false);
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(UsuarioAuditoriaHelper.Resolver(usuario, null));
            RespuestaIniciarPago enlace = await pagos().IniciarPago(new SolicitudPagoTPV
            {
                Empresa = empresa,
                Cliente = Limpiar(primera.Cliente),
                Contacto = Limpiar(ficha.Contacto),
                Importe = total,
                Descripcion = ConceptoPagoPedidoCursos(pedido),
                Correo = null,
                Documento = DocumentoPago(primera.Id),
                Vendedor = Limpiar(ficha.Vendedor)
            }, UsuarioAuditoriaHelper.Resolver(usuario, usuarioAuditoria)).ConfigureAwait(false);

            foreach (EventoInscripcion inscripcion in lista)
            {
                inscripcion.PagoTPVId = enlace.IdPago;
                inscripcion.FormaPago = FormaPagoInscripcionEvento.Tarjeta.ToString();
            }
            await db.SaveChangesAsync().ConfigureAwait(false);
            InscripcionEventoDTO dto = Componer(primera, eventos[primera.EventoId], ficha.Nombre?.Trim());
            dto.UrlPago = enlace.UrlPaginaPago;
            return dto;
        }

        /// <summary>«Pago pedido 930400 cursos»: el pedido en el concepto lo hace enlace del pedido (#679, EnlacesPagoPedido).</summary>
        internal static string ConceptoPagoPedidoCursos(int pedido) => $"Pago pedido {pedido} cursos";

        /// <summary>
        /// NestoAPI#611: llega una señal (#591) por transferencia. Si el cliente tenía una inscripción sin pagar en el evento,
        /// pasa a Pagada con esa señal; si no tenía ninguna, se crea con origen Cobro, ya pagada. Si ya estaba pagada,
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
            // 29/10/26 (primera señal real, cliente 22709): el cobro era un enlace de tarjeta y la inscripción decía «Transferencia».
            // La forma de pago sale del apunte que es la señal (TAR → Tarjeta, EFC → Efectivo, el resto → Transferencia), y el
            // origen es Cobro (no Transferencia): llegó el dinero sin inscripción previa, fuera por donde fuera.
            string formaPagoApunte = null;
            try
            {
                formaPagoApunte = await db.ExtractosCliente
                    .Where(e => e.Nº_Orden == senal.NumOrdenExtracto)
                    .Select(e => e.FormaPago)
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Sin poder leer el apunte, como antes: Transferencia (es solo la etiqueta de la inscripción).
            }
            EventoInscripcion inscripcion = await PagarOInscribir(evento, Limpiar(senal.Empresa), cliente, contacto, OrigenInscripcionEvento.Cobro,
                FormaPagoDeLaSenal(formaPagoApunte), senal.Importe, ahora, usuarioAuditoria, senal.Id).ConfigureAwait(false);
            return Componer(inscripcion, evento, null);
        }

        /// <summary>La forma de pago de la inscripción según la del apunte que es la señal.</summary>
        internal static FormaPagoInscripcionEvento FormaPagoDeLaSenal(string formaPagoApunte)
        {
            string forma = formaPagoApunte?.Trim().ToUpperInvariant();
            if (forma == Constantes.FormasPago.TARJETA)
            {
                return FormaPagoInscripcionEvento.Tarjeta;
            }
            return forma == Constantes.FormasPago.EFECTIVO ? FormaPagoInscripcionEvento.Efectivo : FormaPagoInscripcionEvento.Transferencia;
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

            Cliente ficha = await LeerFichaCliente(empresa, cliente, Limpiar(peticion.Contacto)).ConfigureAwait(false);
            string contacto = Limpiar(ficha.Contacto);

            // Carlos (22/10/26): la referencia de la combinación comprada (o la del producto) dice el curso y, con el sufijo NR, la
            // modalidad; si no la dice, el importe. El mismo reconocedor sirve para otros canales.
            CursoReconocido reconocido = ReconocedorCursoComprado.Reconocer(Limpiar(peticion.Combinacion) ?? producto);
            Evento evento;
            if (reconocido.EventoId.HasValue)
            {
                // EVENTO00123 / EVENTO00123NR dicen el evento
                if (peticion.EventoId.HasValue && peticion.EventoId.Value != reconocido.EventoId.Value)
                {
                    throw new NestoBusinessException($"La referencia {producto} es del evento {reconocido.EventoId.Value}, no del {peticion.EventoId.Value}{pedidoTienda}.");
                }
                int id = reconocido.EventoId.Value;
                evento = await db.Eventos.SingleOrDefaultAsync(e => e.Id == id).ConfigureAwait(false);
                if (evento == null || Limpiar(evento.Empresa) != empresa || !evento.Activo || evento.Fecha.Date < fecha.Date)
                {
                    throw NoEncontrado($"El evento {id} de la referencia {producto} no existe, no está activo o ya ha pasado{pedidoTienda}: revise el pedido a mano.");
                }
            }
            else
            {
                // Producto forzado: puede ser de varios eventos (fechas o tiendas distintas). La referencia, en memoria: los char
                // vienen con relleno (los eventos activos son pocos)
                List<Evento> activos = (await db.Eventos.Where(e => e.Empresa == empresa && e.Activo && e.ProductoCurso != null)
                    .ToListAsync().ConfigureAwait(false))
                    .Where(e => e.Fecha.Date >= fecha.Date)
                    .ToList();
                List<Evento> eventos = activos.Where(e => Limpiar(e.ProductoCurso) == reconocido.Producto).ToList();
                if (!eventos.Any() && reconocido.Modalidad.HasValue)
                {
                    // Un producto de Nesto que de verdad acaba en NR: la referencia tal cual, sin modalidad
                    string tal = Limpiar(peticion.Combinacion) ?? producto;
                    eventos = activos.Where(e => Limpiar(e.ProductoCurso) == tal).ToList();
                    if (eventos.Any())
                    {
                        reconocido = new CursoReconocido { Producto = tal };
                    }
                }
                evento = await ElegirEventoDeLaTienda(eventos, peticion.EventoId, cliente, contacto, producto, pedidoTienda).ConfigureAwait(false);
            }
            ModalidadInscripcionEvento modalidad = ReconocedorCursoComprado.Modalidad(reconocido, evento, importe, out bool sinCoincidencia);
            if (sinCoincidencia)
            {
                ElmahHelper.Log(new Exception($"[Cursos de la tienda #614] El cliente {cliente} ha pagado {importe:0.00} € por {producto} " +
                    $"(«{evento.Titulo?.Trim()}»){pedidoTienda} y ni la referencia ni el importe dicen la modalidad: se toma como reembolsable."));
            }
            EventoInscripcion inscripcion = await PagarOInscribir(evento, empresa, cliente, contacto, OrigenInscripcionEvento.PrestaShop,
                forma, importe, fecha, UsuarioDe(usuario), null, modalidad).ConfigureAwait(false);
            return (await Completar(new List<EventoInscripcion> { inscripcion }, new Dictionary<int, Evento> { [evento.Id] = evento })
                .ConfigureAwait(false)).Single();
        }

        /// <summary>
        /// Con un producto forzado compartido, el evento de la compra: el que diga Nesto (EventoId); si no, el único; si hay
        /// varios, aquel en el que el cliente tiene una inscripción sin pagar; y si no, el más cercano (queda en ELMAH).
        /// </summary>
        private async Task<Evento> ElegirEventoDeLaTienda(List<Evento> eventos, int? eventoId, string cliente, string contacto,
            string producto, string pedidoTienda)
        {
            if (eventoId.HasValue)
            {
                return eventos.FirstOrDefault(e => e.Id == eventoId.Value)
                    ?? throw NoEncontrado($"El evento {eventoId.Value} no está activo y por venir con la referencia {producto}{pedidoTienda}: revise el pedido a mano.");
            }
            if (!eventos.Any())
            {
                throw NoEncontrado($"No hay ningún evento activo y por venir con la referencia {producto}{pedidoTienda}: revise el pedido a mano.");
            }
            if (eventos.Count == 1)
            {
                return eventos[0];
            }
            List<int> ids = eventos.Select(e => e.Id).ToList();
            List<int> conReserva = (await db.EventosInscripciones.Where(i => ids.Contains(i.EventoId) && i.Cliente == cliente)
                .ToListAsync().ConfigureAwait(false))
                .Where(i => Limpiar(i.Contacto) == contacto
                    && (EstadoDe(i) == EstadoInscripcionEvento.Reservada || EstadoDe(i) == EstadoInscripcionEvento.Caducada))
                .Select(i => i.EventoId).Distinct().ToList();
            if (conReserva.Count == 1)
            {
                return eventos.Single(e => e.Id == conReserva[0]);
            }
            Evento cercano = eventos.OrderBy(e => e.Fecha).ThenBy(e => e.Id).First();
            ElmahHelper.Log(new Exception($"[Cursos de la tienda #614] {producto} es de {eventos.Count} eventos (" +
                string.Join(", ", eventos.OrderBy(e => e.Fecha).Select(e => $"{e.Id} del {e.Fecha:dd/MM/yyyy}")) +
                $") y el cliente {cliente} no tiene reserva en uno solo{pedidoTienda}: se inscribe en el más cercano, el {cercano.Id}."));
            return cercano;
        }

        /// <summary>
        /// El núcleo de las inscripciones que llegan ya pagadas (señal por transferencia, compra en la tienda): si el cliente ya
        /// estaba pagado en el evento, esa (con la señal apuntada si no la tenía); si tenía una sin pagar, se paga; si no tenía
        /// ninguna, se crea con <paramref name="origen"/> y se paga.
        /// </summary>
        private async Task<EventoInscripcion> PagarOInscribir(Evento evento, string empresa, string cliente, string contacto, OrigenInscripcionEvento origen,
            FormaPagoInscripcionEvento forma, decimal importe, DateTime fecha, string usuarioAuditoria, int? eventoSenalId,
            ModalidadInscripcionEvento? modalidad = null)
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
                    // NestoAPI#690: la que se ha comprado o, si no se sabe, la modalidad cuyo importe coincide con lo cobrado (si
                    // no, la reembolsable, como hasta ahora)
                    Modalidad = (modalidad ?? ReglasModalidadEvento.PorImporte(evento, importe)).ToString(),
                    UsuarioCreacion = usuarioAuditoria,
                    FechaCreacion = ahora,
                    Usuario = usuarioAuditoria,
                    FechaModificacion = ahora
                };
                db.EventosInscripciones.Add(pendiente);
            }
            else if (modalidad.HasValue)
            {
                // La que se ha comprado manda sobre la que eligió al reservar
                pendiente.Modalidad = modalidad.Value.ToString();
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
            AnotarPago(inscripcion, forma, importe, fecha, usuario, eventoSenalId);
            await db.SaveChangesAsync().ConfigureAwait(false);
            await ProcesarPagada(inscripcion, evento).ConfigureAwait(false);
        }

        private void AnotarPago(EventoInscripcion inscripcion, FormaPagoInscripcionEvento forma, decimal importe, DateTime fecha, string usuario,
            int? eventoSenalId)
        {
            Cambiar(inscripcion, EstadoInscripcionEvento.Pagada, usuario);
            inscripcion.FormaPago = forma.ToString();
            inscripcion.ImportePagado = importe;
            inscripcion.FechaPago = fecha;
            inscripcion.UsuarioPago = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            if (eventoSenalId.HasValue)
            {
                inscripcion.EventoSenalId = eventoSenalId;
            }
        }

        /// <summary>El enganche del corte 2 con la inscripción ya guardada como Pagada (sus fallos no deshacen el pago: van a ELMAH).</summary>
        private async Task ProcesarPagada(EventoInscripcion inscripcion, Evento evento)
        {
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
            ComprobarQueNoEsGratuita(inscripcion, evento);
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

        // ---------------- NestoAPI#690: confirmación de asistencia ----------------

        /// <summary>
        /// «Confirmo asistencia» / «No podré ir». El cliente de la tienda (token de TNV), solo por las suyas (Origen Cliente); la
        /// tienda del evento, Administración o Dirección, por cualquiera (Origen Tienda). Independiente del pago: no anula ni
        /// devuelve nada. Hasta el día del evento; se puede cambiar de respuesta.
        /// </summary>
        public async Task<InscripcionEventoDTO> ConfirmarAsistencia(int id, ConfirmarAsistenciaDTO peticion, IPrincipal usuario)
        {
            RespuestaAsistenciaEvento respuesta = ReglasAsistenciaEvento.Respuesta(peticion?.Respuesta)
                ?? throw new NestoBusinessException("Indique la respuesta: Confirma o NoPodraIr.");
            EventoInscripcion inscripcion = await LeerInscripcion(id).ConfigureAwait(false);
            Evento evento = await LeerEvento(inscripcion.EventoId).ConfigureAwait(false);
            string clienteDelToken = PermisosEventos.ClienteDelToken(usuario);
            OrigenRespuestaAsistenciaEvento origen;
            if (clienteDelToken != null)
            {
                if (clienteDelToken != Limpiar(inscripcion.Cliente))
                {
                    throw Prohibido("Solo puede contestar por sus inscripciones.");
                }
                origen = OrigenRespuestaAsistenciaEvento.Cliente;
            }
            else
            {
                ComprobarPuedeMantener(usuario, evento, paraMarcarPagada: true);
                origen = OrigenRespuestaAsistenciaEvento.Tienda;
            }
            string motivo = MotivoNoPuedeContestar(inscripcion, evento);
            if (motivo != null)
            {
                throw new NestoBusinessException(motivo);
            }
            await Contestar(inscripcion, evento, respuesta, origen, UsuarioDe(usuario)).ConfigureAwait(false);
            return Componer(inscripcion, evento, null);
        }

        /// <summary>
        /// Decisión de Carlos (22/10/26): «No podré ir» LIBERA la plaza.
        /// <list type="bullet">
        /// <item>Sin pagar (Reservada): se anula (no hay dinero de por medio y el enlace de pago deja de valer).</item>
        /// <item>Pagada, Facturada o gratuita (Inscrita): la inscripción se queda como está (el dinero no se toca) pero deja de
        /// ocupar plaza (<see cref="ReglasInscripcionEvento.OcupaPlaza"/>). Si cambia de idea y confirma, recupera su plaza
        /// si todavía queda alguna. Las reglas de su modalidad no cambian: no hay devolución de la señal en ninguna (épica #610:
        /// «si no viene, lo pierde»); la Reembolsable pierde el cupón cuando la lista le marca «No asistió» (#613), y si nadie
        /// pasa lista el job la marca «No asistió», no «Asistió».</item>
        /// </list>
        /// </summary>
        private async Task Contestar(EventoInscripcion inscripcion, Evento evento, RespuestaAsistenciaEvento respuesta,
            OrigenRespuestaAsistenciaEvento origen, string usuario)
        {
            EstadoInscripcionEvento estado = EstadoDe(inscripcion);
            if (respuesta == RespuestaAsistenciaEvento.Confirma && !OcupaPlaza(inscripcion) && evento.Plazas.HasValue)
            {
                // Vuelve a ocupar plaza: solo si queda alguna (la suya la puede haber cogido otro)
                int ocupadas = (await db.EventosInscripciones.Where(i => i.EventoId == evento.Id).ToListAsync().ConfigureAwait(false))
                    .Count(i => i.Id != inscripcion.Id && OcupaPlaza(i));
                if (ocupadas >= evento.Plazas.Value)
                {
                    throw new NestoBusinessException($"Lo sentimos: ya no quedan plazas en «{evento.Titulo?.Trim()}». " +
                        "Su plaza quedó libre cuando nos dijo que no podría ir.");
                }
            }
            ApuntarRespuesta(inscripcion, respuesta, origen, usuario);
            if (respuesta == RespuestaAsistenciaEvento.NoPodraIr && estado == EstadoInscripcionEvento.Reservada)
            {
                Cambiar(inscripcion, EstadoInscripcionEvento.Anulada, usuario);
            }
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task<ResultadoAsistenciaEnlace> LeerAsistenciaConClave(Guid clave)
        {
            (EventoInscripcion inscripcion, Evento evento) = await LeerPorClave(clave).ConfigureAwait(false);
            if (inscripcion == null)
            {
                return new ResultadoAsistenciaEnlace { Existe = false, Mensaje = "Este enlace ya no es válido." };
            }
            ResultadoAsistenciaEnlace resultado = Resultado(evento);
            resultado.Mensaje = MotivoNoPuedeContestar(inscripcion, evento);
            return resultado;
        }

        public async Task<ResultadoAsistenciaEnlace> ConfirmarAsistenciaConClave(Guid clave, string respuesta)
        {
            (EventoInscripcion inscripcion, Evento evento) = await LeerPorClave(clave).ConfigureAwait(false);
            if (inscripcion == null)
            {
                return new ResultadoAsistenciaEnlace { Existe = false, Mensaje = "Este enlace ya no es válido." };
            }
            ResultadoAsistenciaEnlace resultado = Resultado(evento);
            RespuestaAsistenciaEvento? leida = ReglasAsistenciaEvento.Respuesta(respuesta);
            if (!leida.HasValue)
            {
                resultado.Mensaje = "Elija «Confirmo asistencia» o «No podré ir».";
                return resultado;
            }
            string motivo = MotivoNoPuedeContestar(inscripcion, evento);
            if (motivo != null)
            {
                resultado.Mensaje = motivo;
                return resultado;
            }
            bool sinPagar = EstadoDe(inscripcion) == EstadoInscripcionEvento.Reservada;
            // Sin usuario: queda que fue el cliente por el enlace del correo
            try
            {
                await Contestar(inscripcion, evento, leida.Value, OrigenRespuestaAsistenciaEvento.Cliente, USUARIO_ENLACE_CORREO).ConfigureAwait(false);
            }
            catch (NestoBusinessException ex)
            {
                resultado.Mensaje = ex.Message;
                return resultado;
            }
            resultado.Apuntada = true;
            resultado.Mensaje = leida.Value == RespuestaAsistenciaEvento.Confirma
                ? $"Gracias: queda confirmada su asistencia a «{resultado.Evento}» el {resultado.FechaEvento:dd/MM/yyyy}{(resultado.HoraInicio != null ? " a las " + resultado.HoraInicio : "")}. Le esperamos."
                : sinPagar
                    ? $"Gracias por avisarnos: queda anulada su inscripción a «{resultado.Evento}» y su plaza queda libre."
                    : $"Gracias por avisarnos: queda apuntado que no podrá ir a «{resultado.Evento}» y su plaza queda libre. Si cambia de idea, puede confirmar con el mismo enlace mientras queden plazas.";
            return resultado;
        }

        private async Task<(EventoInscripcion, Evento)> LeerPorClave(Guid clave)
        {
            if (clave == Guid.Empty)
            {
                return (null, null);
            }
            Guid? buscada = clave;
            EventoInscripcion inscripcion = await db.EventosInscripciones.FirstOrDefaultAsync(i => i.ClaveAsistencia == buscada).ConfigureAwait(false);
            if (inscripcion == null)
            {
                return (null, null);
            }
            Evento evento = await db.Eventos.SingleOrDefaultAsync(e => e.Id == inscripcion.EventoId).ConfigureAwait(false);
            return evento == null ? (null, null) : (inscripcion, evento);
        }

        private static ResultadoAsistenciaEnlace Resultado(Evento evento) => new ResultadoAsistenciaEnlace
        {
            Existe = true,
            Evento = evento.Titulo?.Trim(),
            FechaEvento = evento.Fecha.Date,
            HoraInicio = ReglasModalidadEvento.EscribirHora(evento.HoraInicio)
        };

        /// <summary>Null si todavía se puede contestar; si no, por qué (de usted, para el cliente y para la tienda).</summary>
        internal string MotivoNoPuedeContestar(EventoInscripcion inscripcion, Evento evento)
        {
            EstadoInscripcionEvento estado = EstadoDe(inscripcion);
            if (!ReglasInscripcionEvento.PUEDEN_CONFIRMAR_ASISTENCIA.Contains(estado))
            {
                return $"La inscripción a «{evento.Titulo?.Trim()}» está {ReglasInscripcionEvento.Texto(estado).ToLowerInvariant()}: ya no se puede contestar.";
            }
            if (evento.Fecha.Date < reloj().Date)
            {
                return $"«{evento.Titulo?.Trim()}» ya ha pasado.";
            }
            return null;
        }

        private void ApuntarRespuesta(EventoInscripcion inscripcion, RespuestaAsistenciaEvento respuesta, OrigenRespuestaAsistenciaEvento origen, string usuario)
        {
            DateTime ahora = reloj();
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            inscripcion.AsistenciaRespuesta = respuesta.ToString();
            inscripcion.AsistenciaFecha = ahora;
            inscripcion.AsistenciaUsuario = usuarioAuditoria;
            inscripcion.AsistenciaOrigen = origen.ToString();
            inscripcion.Usuario = usuarioAuditoria;
            inscripcion.FechaModificacion = ahora;
        }

        // ---------------- NestoAPI#690: recordatorio ----------------

        /// <summary>
        /// Las inscripciones a las que les toca el recordatorio hoy: las que esperan al evento (pagadas o gratuitas) de los
        /// eventos activos de dentro de <paramref name="diasAntes"/> días y, si un día no corrió el job, también las de antes
        /// de esa fecha (mañana), sin recordatorio todavía. A cada una le pone su clave para el enlace del correo.
        /// </summary>
        internal async Task<List<KeyValuePair<Evento, List<EventoInscripcion>>>> PendientesDeRecordatorio(int diasAntes)
        {
            DateTime hoy = reloj().Date;
            DateTime desde = hoy.AddDays(1);
            DateTime hasta = hoy.AddDays(diasAntes);
            List<Evento> eventos = await db.Eventos
                .Where(e => e.Activo && e.Fecha >= desde && e.Fecha <= hasta)
                .ToListAsync().ConfigureAwait(false);
            var resultado = new List<KeyValuePair<Evento, List<EventoInscripcion>>>();
            if (!eventos.Any())
            {
                return resultado;
            }
            List<int> ids = eventos.Select(e => e.Id).ToList();
            List<string> estados = ReglasInscripcionEvento.ESPERAN_AL_EVENTO.Select(e => e.ToString()).ToList();
            List<EventoInscripcion> inscripciones = await db.EventosInscripciones
                .Where(i => ids.Contains(i.EventoId) && estados.Contains(i.Estado) && i.RecordatorioEnviado == null)
                .ToListAsync().ConfigureAwait(false);
            bool cambios = false;
            foreach (Evento evento in eventos.OrderBy(e => e.Fecha))
            {
                // Quien ya dijo «No podré ir» ha liberado su plaza: no se le recuerda
                List<EventoInscripcion> suyas = inscripciones.Where(i => i.EventoId == evento.Id && OcupaPlaza(i)).ToList();
                if (!suyas.Any())
                {
                    continue;
                }
                foreach (EventoInscripcion inscripcion in suyas.Where(i => i.ClaveAsistencia == null))
                {
                    inscripcion.ClaveAsistencia = Guid.NewGuid();
                    cambios = true;
                }
                resultado.Add(new KeyValuePair<Evento, List<EventoInscripcion>>(evento, suyas));
            }
            if (cambios)
            {
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            return resultado;
        }

        /// <summary>Apunta que ya salió el recordatorio (una sola vez por inscripción).</summary>
        internal async Task MarcarRecordatorioEnviado(IEnumerable<EventoInscripcion> inscripciones)
        {
            DateTime ahora = reloj();
            foreach (EventoInscripcion inscripcion in inscripciones ?? Enumerable.Empty<EventoInscripcion>())
            {
                inscripcion.RecordatorioEnviado = ahora;
            }
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        // ---------------- Decisión de Carlos (22/10/26): plazo de pago de las reservas ----------------

        /// <summary>
        /// Las reservas sin pagar a las que toca avisar hoy de que su plaza se libera: eventos activos por venir, sin aviso todavía
        /// y con el último día para pagar (<see cref="ReglasInscripcionEvento.LimitePagoEfectivo"/>) a dos días o menos. Con la
        /// fecha en que se liberará (el día después del límite, y nunca antes de mañana: siempre se avisa antes de liberar) y
        /// el enlace de pago si lo tiene.
        /// </summary>
        internal async Task<List<AvisoPagoInscripcion>> PendientesDeAvisoPago()
        {
            DateTime hoy = reloj().Date;
            List<KeyValuePair<Evento, EventoInscripcion>> reservas = await ReservasVivas(hoy).ConfigureAwait(false);
            var avisos = new List<AvisoPagoInscripcion>();
            foreach (KeyValuePair<Evento, EventoInscripcion> r in reservas.Where(r => r.Value.AvisoPagoEnviado == null))
            {
                DateTime? limite = ReglasInscripcionEvento.LimitePagoEfectivo(r.Value, r.Key);
                if (limite == null || hoy < limite.Value.AddDays(-ReglasInscripcionEvento.DIAS_AVISO_ANTES_DE_VENCER))
                {
                    continue;
                }
                DateTime liberacion = (limite.Value < hoy ? hoy : limite.Value).AddDays(1);
                avisos.Add(new AvisoPagoInscripcion { Evento = r.Key, Inscripcion = r.Value, FechaLimite = limite.Value, FechaLiberacion = liberacion });
            }
            List<int> pagos = avisos.Where(a => a.Inscripcion.PagoTPVId.HasValue).Select(a => a.Inscripcion.PagoTPVId.Value).Distinct().ToList();
            if (pagos.Any())
            {
                Dictionary<int, Guid> tokens = (await db.PagosTPV.Where(p => pagos.Contains(p.Id))
                    .Select(p => new { p.Id, p.TokenAcceso }).ToListAsync().ConfigureAwait(false))
                    .ToDictionary(p => p.Id, p => p.TokenAcceso);
                foreach (AvisoPagoInscripcion aviso in avisos.Where(a => a.Inscripcion.PagoTPVId.HasValue))
                {
                    if (tokens.TryGetValue(aviso.Inscripcion.PagoTPVId.Value, out Guid token))
                    {
                        aviso.UrlPago = URL_PAGINA_PAGO + token;
                    }
                }
            }
            return avisos;
        }

        /// <summary>Apunta que ya salió el aviso de pago (una sola vez por inscripción).</summary>
        internal async Task MarcarAvisoPagoEnviado(IEnumerable<EventoInscripcion> inscripciones)
        {
            DateTime ahora = reloj();
            foreach (EventoInscripcion inscripcion in inscripciones ?? Enumerable.Empty<EventoInscripcion>())
            {
                inscripcion.AvisoPagoEnviado = ahora;
            }
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Pasado el último día para pagar, la reserva avisada (el aviso salió otro día) se libera: pasa a Caducada, el mismo
        /// estado que la del plazo de siempre (no ocupa plaza y, si paga tarde, el dinero manda). Las que vencen por la fecha
        /// guardada las sigue caducando el job de las 7:55 (<see cref="CaducarVencidas"/>), avisadas o no, como hasta ahora.
        /// </summary>
        internal async Task<List<InscripcionEventoDTO>> LiberarReservasVencidas()
        {
            DateTime hoy = reloj().Date;
            List<KeyValuePair<Evento, EventoInscripcion>> reservas = await ReservasVivas(hoy).ConfigureAwait(false);
            List<KeyValuePair<Evento, EventoInscripcion>> vencidas = reservas
                .Where(r => r.Value.AvisoPagoEnviado.HasValue && r.Value.AvisoPagoEnviado.Value.Date < hoy)
                .Where(r => ReglasInscripcionEvento.LimitePagoEfectivo(r.Value, r.Key) is DateTime limite && limite < hoy)
                .ToList();
            if (!vencidas.Any())
            {
                return new List<InscripcionEventoDTO>();
            }
            foreach (KeyValuePair<Evento, EventoInscripcion> r in vencidas)
            {
                Cambiar(r.Value, EstadoInscripcionEvento.Caducada, USUARIO_AUTOMATICO);
            }
            await db.SaveChangesAsync().ConfigureAwait(false);
            Dictionary<int, Evento> eventos = vencidas.GroupBy(r => r.Key.Id).ToDictionary(g => g.Key, g => g.First().Key);
            return await Completar(vencidas.Select(r => r.Value).ToList(), eventos).ConfigureAwait(false);
        }

        /// <summary>Las reservas sin pagar (que no dijeron «No podré ir») de los eventos activos de hoy en adelante.</summary>
        private async Task<List<KeyValuePair<Evento, EventoInscripcion>>> ReservasVivas(DateTime hoy)
        {
            List<Evento> eventos = await db.Eventos.Where(e => e.Activo && e.Fecha >= hoy).ToListAsync().ConfigureAwait(false);
            if (!eventos.Any())
            {
                return new List<KeyValuePair<Evento, EventoInscripcion>>();
            }
            List<int> ids = eventos.Select(e => e.Id).ToList();
            string reservada = EstadoInscripcionEvento.Reservada.ToString();
            List<EventoInscripcion> inscripciones = await db.EventosInscripciones
                .Where(i => ids.Contains(i.EventoId) && i.Estado == reservada).ToListAsync().ConfigureAwait(false);
            return inscripciones.Where(OcupaPlaza)
                .Select(i => new KeyValuePair<Evento, EventoInscripcion>(eventos.First(e => e.Id == i.EventoId), i))
                .ToList();
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
                throw new NestoBusinessException("Estas inscripciones no están pagadas; márquelas como pagadas antes de pasar lista: " +
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

            List<InscripcionEventoDTO> lista = await Completar(inscripciones, new Dictionary<int, Evento> { [evento.Id] = evento }).ConfigureAwait(false);
            await RellenarBaseImponibleDia(lista, evento).ConfigureAwait(false);
            return lista.OrderBy(i => OrdenEstado(i.Estado)).ThenBy(i => i.Nombre).ToList();
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
            List<string> pagadas = ReglasInscripcionEvento.ESPERAN_AL_EVENTO.Select(e => e.ToString()).ToList();
            List<EventoInscripcion> inscripciones = await db.EventosInscripciones
                .Where(i => ids.Contains(i.EventoId) && pagadas.Contains(i.Estado)).ToListAsync().ConfigureAwait(false);
            var noVinieron = new List<int>();

            foreach (Evento evento in eventos.Where(e => e.Fecha.Date < hoy).OrderBy(e => e.Fecha))
            {
                List<EventoInscripcion> suyas = inscripciones.Where(i => i.EventoId == evento.Id).ToList();
                if (!suyas.Any())
                {
                    continue;
                }
                foreach (EventoInscripcion inscripcion in suyas)
                {
                    // Decisión de Carlos (22/10/26): quien dijo «No podré ir» no se da por asistido (pierde el cupón, #613)
                    bool avisoQueNoVenia = !OcupaPlaza(inscripcion);
                    Cambiar(inscripcion, avisoQueNoVenia ? EstadoInscripcionEvento.NoAsistio : EstadoInscripcionEvento.Asistio, USUARIO_AUTOMATICO);
                    if (avisoQueNoVenia)
                    {
                        noVinieron.Add(inscripcion.Id);
                    }
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
            if (noVinieron.Any() && aplicarAsistenciaCupones != null)
            {
                try
                {
                    await aplicarAsistenciaCupones(noVinieron.Select(id => new KeyValuePair<int, bool>(id, false)).ToList(), USUARIO_AUTOMATICO)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Cupones #613] No se han podido anular los cupones de quienes dijeron que no podrían ir " +
                        $"({string.Join(", ", noVinieron)}): {ex.Message}", ex));
                }
            }
            return resultado;
        }

        // ---------------- Ayudantes ----------------

        /// <summary>NestoAPI#690: una inscripción gratuita no se paga ni se factura (400).</summary>
        private static void ComprobarQueNoEsGratuita(EventoInscripcion inscripcion, Evento evento)
        {
            if (ReglasModalidadEvento.DeInscripcion(inscripcion) == ModalidadInscripcionEvento.Gratuita)
            {
                throw new NestoBusinessException($"La inscripción {inscripcion.Id} es gratuita («{evento?.Titulo?.Trim()}»): no se paga ni se factura.");
            }
        }

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

        private static bool OcupaPlaza(EventoInscripcion inscripcion) => ReglasInscripcionEvento.OcupaPlaza(inscripcion);

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
            var fichas = (await db.Clientes
                .Where(c => empresas.Contains(c.Empresa) && clientes.Contains(c.Nº_Cliente))
                .Select(c => new { c.Nº_Cliente, c.Contacto, c.Nombre, c.Dirección, c.CodPostal, c.Población, c.Provincia })
                .ToListAsync().ConfigureAwait(false))
                .GroupBy(c => Limpiar(c.Nº_Cliente) + "/" + Limpiar(c.Contacto))
                .ToDictionary(g => g.Key, g => g.First());

            // NestoAPI#731: la persona que asiste y las personas de contacto (correo). Si algo falla, la lista sale igual sin ello.
            Dictionary<int, string> asistentes = await LeerAsistentesSinFallar(inscripciones.Select(i => i.Id).ToList()).ConfigureAwait(false);
            List<KeyValuePair<string, PersonaContactoEventoDTO>> personas;
            try
            {
                personas = await LeerPersonasDe(empresas, clientes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Eventos #731] No se han podido leer las personas de contacto de los inscritos: {ex.Message}", ex));
                personas = new List<KeyValuePair<string, PersonaContactoEventoDTO>>();
            }
            ILookup<string, PersonaContactoEventoDTO> personasPorCliente = personas.ToLookup(p => p.Key, p => p.Value);
            Dictionary<int, int> justificantes = await LeerJustificantesSinFallar(inscripciones.Select(i => i.Id).ToList()).ConfigureAwait(false);

            var porPagar = new HashSet<int>(inscripciones
                .Where(i => i.PagoTPVId.HasValue && (EstadoDe(i) == EstadoInscripcionEvento.Reservada || EstadoDe(i) == EstadoInscripcionEvento.Caducada))
                .Select(i => i.Id));
            List<int> pagosPendientes = inscripciones.Where(i => porPagar.Contains(i.Id))
                .Select(i => i.PagoTPVId.Value).Distinct().ToList();
            // 29/10/26: el enlace sale en la lista de inscritos (y en las del cliente) mientras se puede pagar: inscripción sin
            // pagar y PagoTPV pendiente (ni autorizado, ni anulado, ni devuelto). Una sola consulta para toda la lista.
            Dictionary<int, Guid> tokens = new Dictionary<int, Guid>();
            if (pagosPendientes.Any())
            {
                tokens = (await db.PagosTPV.Where(p => pagosPendientes.Contains(p.Id))
                    .Select(p => new { p.Id, p.TokenAcceso, p.Estado }).ToListAsync().ConfigureAwait(false))
                    .Where(p => EnlaceSePuedePagar(p.Estado))
                    .ToDictionary(p => p.Id, p => p.TokenAcceso);
            }

            Dictionary<int, FacturacionCursoInscripcion> facturaciones = await LeerFacturaciones(inscripciones).ConfigureAwait(false);

            return inscripciones.Select(i =>
            {
                eventos.TryGetValue(i.EventoId, out Evento evento);
                fichas.TryGetValue(Limpiar(i.Cliente) + "/" + Limpiar(i.Contacto), out var ficha);
                InscripcionEventoDTO dto = Componer(i, evento, ficha?.Nombre?.Trim());
                asistentes.TryGetValue(i.Id, out string asiste);
                RellenarFicha(dto, ficha?.Dirección, ficha?.CodPostal, ficha?.Población, ficha?.Provincia,
                    personasPorCliente[Limpiar(i.Cliente) ?? ""], asiste);
                if (justificantes.TryGetValue(i.Id, out int pedidoJustificante))
                {
                    dto.PedidoJustificante = pedidoJustificante;
                }
                if (porPagar.Contains(i.Id) && tokens.TryGetValue(i.PagoTPVId.Value, out Guid token))
                {
                    dto.UrlPago = ServicioPagos.UrlPaginaPago(token);
                }
                if (facturaciones.TryGetValue(i.Id, out FacturacionCursoInscripcion f))
                {
                    dto.Facturacion = FacturacionCursoDTO.De(f);
                }
                return dto;
            }).ToList();
        }

        /// <summary>
        /// El enlace de pago de la inscripción todavía sirve: ni cobrado (Autorizado), ni anulado (Cancelado), ni devuelto. Un
        /// Denegado sí: la página /pago/{token} lleva al reintento.
        /// </summary>
        internal static bool EnlaceSePuedePagar(string estadoPagoTPV)
        {
            string estado = estadoPagoTPV?.Trim();
            return !string.Equals(estado, Constantes.EstadosPagoTPV.AUTORIZADO, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(estado, Constantes.EstadosPagoTPV.CANCELADO, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(estado, Constantes.EstadosPagoTPV.DEVUELTO, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>NestoAPI#731: inscripción → persona que asiste. Sin columna (o si falla): ninguna, y la lista sale igual.</summary>
        private async Task<Dictionary<int, string>> LeerAsistentesSinFallar(List<int> ids)
        {
            if (PersonasAsistentes == null || !ids.Any())
            {
                return new Dictionary<int, string>();
            }
            try
            {
                return await PersonasAsistentes.Leer(ids).ConfigureAwait(false) ?? new Dictionary<int, string>();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Eventos #731] No se ha podido leer quién asiste: {ex.Message}", ex));
                return new Dictionary<int, string>();
            }
        }

        /// <summary>NestoAPI#732: inscripción → pedido que la justifica. Sin columna (o si falla): ninguno, y la lista sale igual.</summary>
        private async Task<Dictionary<int, int>> LeerJustificantesSinFallar(List<int> ids)
        {
            if (ProductosRequeridos == null || !ids.Any())
            {
                return new Dictionary<int, int>();
            }
            try
            {
                return await ProductosRequeridos.LeerJustificantes(ids).ConfigureAwait(false) ?? new Dictionary<int, int>();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Eventos #732] No se han podido leer los pedidos justificantes: {ex.Message}", ex));
                return new Dictionary<int, int>();
            }
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
            ModalidadInscripcionEvento modalidad = ReglasModalidadEvento.DeInscripcion(i);
            EstadoAsistenciaEvento asistencia = ReglasAsistenciaEvento.Estado(i);
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
                FechaCreacion = i.FechaCreacion,
                Modalidad = modalidad,
                ImporteSenal = ReglasModalidadEvento.Importe(evento, modalidad),
                ConCupon = ReglasModalidadEvento.EmiteCupon(modalidad),
                AsistenciaConfirmada = AsistenciaDe(i),
                EstadoAsistencia = asistencia,
                EstadoAsistenciaTexto = ReglasAsistenciaEvento.Texto(asistencia),
                RecordatorioEnviado = i.RecordatorioEnviado
            };
        }

        private static AsistenciaConfirmadaDTO AsistenciaDe(EventoInscripcion i)
        {
            RespuestaAsistenciaEvento? respuesta = ReglasAsistenciaEvento.Respuesta(i.AsistenciaRespuesta);
            if (!respuesta.HasValue)
            {
                return null;
            }
            return new AsistenciaConfirmadaDTO
            {
                Respuesta = respuesta.Value,
                Fecha = i.AsistenciaFecha ?? i.FechaModificacion,
                Usuario = Limpiar(i.AsistenciaUsuario),
                Origen = string.Equals(Limpiar(i.AsistenciaOrigen), OrigenRespuestaAsistenciaEvento.Tienda.ToString(), StringComparison.OrdinalIgnoreCase)
                    ? OrigenRespuestaAsistenciaEvento.Tienda
                    : OrigenRespuestaAsistenciaEvento.Cliente
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
