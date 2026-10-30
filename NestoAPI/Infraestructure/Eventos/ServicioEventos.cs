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
        /// <summary>
        /// La señal REEMBOLSABLE: factura exenta del curso y cupón por ese importe, IVA incluido (#613). NestoAPI#690: al crear,
        /// si no viene en el cuerpo, 100 € (un 0 explícito es un 0).
        /// </summary>
        public decimal ImporteSenal { get => importeSenal; set { importeSenal = value; recibidos.Add(nameof(ImporteSenal)); } }
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
        private decimal importeSenal;
        private decimal importeSenalNoReembolsable;
        private string horaInicio;
        private string horaFin;
        private string ponente;
        private int? diasPagoAntesEvento;
        private string formato;

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
        /// <summary>
        /// NestoAPI#746: solo lectura. De <see cref="PlazasOcupadas"/>, cuántas son reservas sin pagar (Reservada que no ha dicho
        /// «No podré ir»). Con el evento lleno, esas reciben el aviso para pagar o perder la plaza.
        /// </summary>
        public int PlazasSinPagar { get; set; }
        /// <summary>NestoAPI#611 (para TNV).</summary>
        public string Lugar { get => lugar; set { lugar = value; recibidos.Add(nameof(Lugar)); } }
        /// <summary>
        /// NestoAPI#611 (para TNV): la columna Eventos.Descripcion. NO se llama «Descripcion» porque el EventoModel del Nesto de
        /// #591 ya manda una «Descripcion» calculada («Masterclass Cloasma 13/10/2026») y se guardaría como texto del evento.
        /// </summary>
        public string DescripcionLarga { get => descripcionLarga; set { descripcionLarga = value; recibidos.Add(nameof(DescripcionLarga)); } }
        /// <summary>NestoAPI#611: solo lectura. Cuándo se pasó lista (null = todavía no).</summary>
        public DateTime? FechaListaAsistencia { get; set; }

        /// <summary>NestoAPI#690: «HH:mm» (por ejemplo, «10:00»); null = sin horario.</summary>
        public string HoraInicio { get => horaInicio; set { horaInicio = value; recibidos.Add(nameof(HoraInicio)); } }
        /// <summary>NestoAPI#690: «HH:mm», posterior a <see cref="HoraInicio"/>.</summary>
        public string HoraFin { get => horaFin; set { horaFin = value; recibidos.Add(nameof(HoraFin)); } }
        /// <summary>NestoAPI#690: texto libre, hasta 100 caracteres.</summary>
        public string Ponente { get => ponente; set { ponente = value; recibidos.Add(nameof(Ponente)); } }
        /// <summary>
        /// NestoAPI#690: la señal NO reembolsable: factura exenta del curso y sin cupón. Al crear, si no viene en el cuerpo,
        /// 60 €. Con las dos señales a 0, el evento es gratuito.
        /// </summary>
        public decimal ImporteSenalNoReembolsable
        {
            get => importeSenalNoReembolsable;
            set { importeSenalNoReembolsable = value; recibidos.Add(nameof(ImporteSenalNoReembolsable)); }
        }
        /// <summary>NestoAPI#690: solo lectura. Las dos señales a 0.</summary>
        public bool Gratuito { get; set; }
        /// <summary>NestoAPI#690: solo lectura. Lo que se puede elegir al inscribirse (Reembolsable, NoReembolsable o Gratuita).</summary>
        public List<string> Modalidades { get; set; } = new List<string>();
        /// <summary>NestoAPI#690: solo lectura. La imagen del evento (pública, para campañas); null si no tiene.</summary>
        public string ImagenUrl { get; set; }
        /// <summary>
        /// Carlos (22/10/26): cuántos días antes del evento vence el pago de una reserva; null = 7. Con
        /// Eventos:LiberarReservasSinPagar, dos días antes de vencer se avisa y al vencer la plaza se libera.
        /// </summary>
        public int? DiasPagoAntesEvento
        {
            get => diasPagoAntesEvento;
            set { diasPagoAntesEvento = value; recibidos.Add(nameof(DiasPagoAntesEvento)); }
        }
        /// <summary>
        /// Carlos (22/10/26): «Presencial» u «Online» (webinar). Al leer, nunca null (los de antes, Presencial). Al escribir, si no
        /// viene, Presencial al crear y el que tuviera al modificar.
        /// </summary>
        public string Formato { get => formato; set { formato = value; recibidos.Add(nameof(Formato)); } }

        /// <summary>
        /// NestoAPI#692: solo lectura. Tiene todo lo necesario para anunciarlo en las apps (ver
        /// <see cref="ReglasAnuncioEvento.QueFalta"/>, imagen incluida).
        /// </summary>
        public bool Completo { get; set; }
        /// <summary>NestoAPI#692: solo lectura. Lo que le falta para anunciarlo («la imagen», «el ponente»…); vacía si está completo.</summary>
        public List<string> QueFalta { get; set; } = new List<string>();
        /// <summary>NestoAPI#692: solo lectura. Cuándo se anunció la última vez (null = nunca).</summary>
        public DateTime? AnunciadoFecha { get; set; }
        /// <summary>NestoAPI#692: solo lectura. Quién lo anunció la última vez.</summary>
        public string AnunciadoUsuario { get; set; }

        private List<ProductoRequeridoEventoDTO> productosRequeridos = new List<ProductoRequeridoEventoDTO>();
        /// <summary>
        /// NestoAPI#732: evento «gratuito por compra». Los productos que el cliente tiene que haber comprado o pedido para
        /// apuntarse (basta con uno). Al leer, siempre una lista (vacía si no exige compra), con Producto y Nombre. Al escribir
        /// (POST/PUT), solo si viene: la lista que venga sustituye a la que hubiera (vacía = deja de exigir compra); null o sin
        /// el campo, no se toca. Se manda solo Producto; Nombre se ignora.
        /// </summary>
        // 29/10/26: Replace para que Json.NET cree la lista y llame al setter (con Auto rellenaba la lista ya creada sin
        // pasar por él, TraeProductosRequeridos() daba false y los productos que mandaba Nesto no se guardaban: evento 4).
        [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
        public List<ProductoRequeridoEventoDTO> ProductosRequeridos
        {
            get => productosRequeridos;
            set { productosRequeridos = value; recibidos.Add(nameof(ProductosRequeridos)); }
        }

        /// <summary>NestoAPI#732: ¿viene la lista de productos requeridos para cambiarla? (null no cuenta).</summary>
        public bool TraeProductosRequeridos() => Trae(nameof(ProductosRequeridos)) && productosRequeridos != null;

        private string productosRequeridosModo = ReglasProductosRequeridosEvento.MODO_ALGUNO;
        /// <summary>
        /// NestoAPI#732 (Carlos, 29/10/26): «Alguno» (basta con uno de los productos requeridos; por defecto, lo de siempre) o
        /// «Todos» (hay que haber comprado o pedido cada uno). Al leer, siempre uno de los dos. Al escribir (POST/PUT), solo si
        /// viene: null o sin el campo, no se toca; otro valor, 400.
        /// </summary>
        public string ProductosRequeridosModo
        {
            get => productosRequeridosModo;
            set { productosRequeridosModo = value; recibidos.Add(nameof(ProductosRequeridosModo)); }
        }

        /// <summary>NestoAPI#732: ¿viene el modo para cambiarlo? (null no cuenta).</summary>
        public bool TraeProductosRequeridosModo() => Trae(nameof(ProductosRequeridosModo)) && productosRequeridosModo != null;

        /// <summary>
        /// NestoAPI#613 (Carlos, 29/10/26): el pedido mínimo (IVA incluido, solo productos) para usar el cupón que gana quien paga la
        /// señal reembolsable; se copia al cupón al emitirlo. Al leer, siempre un importe (0 = cualquier importe: los eventos de
        /// antes y los que no lo informan). Al escribir (POST/PUT), si viene se guarda; null o sin el campo, no se toca. Negativo,
        /// 400; mayor que 0 sin el script Issue613_CuponPedidoMinimoPorEvento.sql, 400.
        /// </summary>
        public decimal? PedidoMinimoCupon { get; set; }
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
        /// <summary>NestoAPI#692: un evento con sus plazas ocupadas, su imagen, si está completo y si se ha anunciado. Null si no existe.</summary>
        Task<EventoDTO> LeerEvento(int id);
        /// <summary>NestoAPI#611: el AlmacénPedidoVta del usuario (para saber de qué tienda es). Null si no tiene.</summary>
        string AlmacenDelUsuario(IPrincipal usuario, string empresa);
        /// <summary>NestoAPI#611: la tienda del evento guardado ("" si no tiene); null si el evento no existe.</summary>
        Task<string> AlmacenDelEvento(int id);
        Task<EventoDTO> CrearEvento(EventoDTO evento, string usuario);
        Task<EventoDTO> ModificarEvento(int id, EventoDTO evento, string usuario);
        Task<List<SenalEventoDTO>> LeerSenales(string empresa, string estado, string cliente, int? eventoId);
        Task<List<SenalEventoDTO>> LeerSenalesCliente(string empresa, string cliente, string contacto);
        Task<SenalEventoDTO> MarcarSenal(int eventoId, MarcarSenalEventoDTO peticion, string usuario);
        Task QuitarSenal(int id, string usuario);
        /// <summary>NestoAPI#690: sustituye la imagen del evento y devuelve el evento con su ImagenUrl nueva.</summary>
        Task<EventoDTO> GuardarImagen(int id, ImagenEventoAGrabar imagen, string usuario);
        /// <summary>NestoAPI#690: false si el evento no tenía imagen.</summary>
        Task<bool> BorrarImagen(int id);
        /// <summary>NestoAPI#690: la imagen (con el contenido), o null si no tiene.</summary>
        Task<ImagenEvento> LeerImagen(int id);
        /// <summary>
        /// Carlos (22/10/26, Nesto#517): da de alta (si no existe) la referencia del curso del evento para esa modalidad
        /// (EVENTO00123 / EVENTO00123NR) y la liga al evento. Idempotente.
        /// </summary>
        Task<ProductoCursoDTO> CrearProductoCurso(int id, PeticionProductoCursoDTO peticion, string usuario);
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
        public const int LONGITUD_PONENTE = 100;
        public const string MENSAJE_FALTA_TIENDA =
            "Indique la tienda del evento: Reina (REI) o Alcobendas (ALC). También en los eventos online, porque el ponente emite desde una de las dos tiendas.";
        /// <summary>El CHECK de Eventos.DiasPagoAntesEvento (Issue690).</summary>
        public const int MAXIMO_DIAS_PAGO = 90;

        /// <summary>NestoAPI#612: DELETE api/Eventos/Senales/{id} de una señal que ya facturó el curso o emitió el cupón (409).</summary>
        public const string ERROR_SENAL_YA_USADA = "SENAL_EVENTO_YA_USADA";

        public const string MENSAJE_SIN_PERMISO_SENALES =
            "Las señales de los eventos las marca Administración. Pídaselo a Informática si necesita hacerlo.";

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
        private readonly Func<Func<Task>, Task> enTransaccion;
        private readonly IRepositorioImagenesEventos imagenes;
        private readonly IRepositorioProductoCurso productosCurso;
        private readonly IRepositorioAnunciosEventos anuncios;

        public ServicioEventos(NVEntities db) : this(db, () => DateTime.Now, imagenes: new RepositorioImagenesEventos(db),
            anuncios: new RepositorioAnunciosEventosSql(db))
        {
            ProductosRequeridos = new RepositorioProductosRequeridosEventos(db);
            PedidoMinimoCupon = new RepositorioPedidoMinimoCuponEventos(db);
        }

        /// <summary>
        /// NestoAPI#732: los productos requeridos de los eventos «gratuitos por compra» (fuera del EDMX). Null (tests): los eventos
        /// salen sin ellos y un POST/PUT que los traiga contesta que falta el script.
        /// </summary>
        internal IRepositorioProductosRequeridosEventos ProductosRequeridos { get; set; }

        /// <summary>
        /// NestoAPI#613: el pedido mínimo del cupón de cada evento (fuera del EDMX). Null (tests): los eventos salen con 0 y un
        /// POST/PUT con más de 0 contesta que falta el script.
        /// </summary>
        internal IRepositorioPedidoMinimoCuponEventos PedidoMinimoCupon { get; set; }

        /// <param name="almacenDelUsuario">(empresa, usuario sin dominio) → su AlmacénPedidoVta. Null: el de ParametrosUsuario.</param>
        /// <param name="imagenes">NestoAPI#690: las imágenes de los eventos. Null: los eventos salen sin imagen y no se pueden subir.</param>
        internal ServicioEventos(NVEntities db, Func<DateTime> reloj, Func<string, string, string> almacenDelUsuario = null,
            IRepositorioFacturacionCursos facturacionCursos = null, IRepositorioCupones cupones = null,
            Func<Func<Task>, Task> enTransaccion = null, IRepositorioImagenesEventos imagenes = null,
            IRepositorioProductoCurso productosCurso = null, IRepositorioAnunciosEventos anuncios = null)
        {
            this.imagenes = imagenes;
            this.anuncios = anuncios;
            this.productosCurso = productosCurso ?? new RepositorioProductoCurso(db);
            this.db = db;
            this.reloj = reloj;
            this.almacenDelUsuario = almacenDelUsuario
                ?? ((empresa, usuario) => Controllers.ParametrosUsuarioController.LeerParametro(empresa, usuario, PermisosEventos.CLAVE_ALMACEN_USUARIO));
            this.facturacionCursos = facturacionCursos ?? new RepositorioFacturacionCursos(db);
            this.cupones = cupones ?? new RepositorioCupones(db);
            this.enTransaccion = enTransaccion ?? EnTransaccionBd;
        }

        private async Task EnTransaccionBd(Func<Task> trabajo)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                await trabajo().ConfigureAwait(false);
                transaccion.Commit();
            }
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
            Dictionary<int, DateTime> conImagen = await FechasImagenes(eventos.Select(e => e.Id).ToList()).ConfigureAwait(false);
            Dictionary<int, AnuncioEventoLeido> anunciados = await Anunciados(eventos.Select(e => e.Id).ToList()).ConfigureAwait(false);
            DateTime hoy = reloj().Date;
            List<EventoDTO> lista = eventos.Where(e => e.Fecha.Date >= hoy).OrderBy(e => e.Fecha).ThenBy(e => e.Titulo)
                .Concat(eventos.Where(e => e.Fecha.Date < hoy).OrderByDescending(e => e.Fecha).ThenBy(e => e.Titulo))
                .Select(e => ConAnuncio(ConImagen(ADTO(e, ocupadas.TryGetValue(e.Id, out int n) ? n : 0), conImagen), anunciados))
                .ToList();
            await ConPlazasSinPagar(lista).ConfigureAwait(false);
            return await ConProductosRequeridos(lista).ConfigureAwait(false);
        }

        private static int requeridosFalloRegistrado;

        /// <summary>
        /// NestoAPI#732: los productos requeridos de cada evento, en UNA consulta. Si falla, los eventos salen sin ellos y el
        /// fallo va a ELMAH una vez (sin la tabla, el repositorio ya devuelve vacío).
        /// </summary>
        private async Task<List<EventoDTO>> ConProductosRequeridos(List<EventoDTO> lista)
        {
            await ConPedidoMinimoCupon(lista).ConfigureAwait(false);
            if (ProductosRequeridos == null || lista == null || !lista.Any())
            {
                return lista;
            }
            try
            {
                Dictionary<int, List<ProductoRequeridoLeido>> requeridos = await ProductosRequeridos
                    .Leer(lista.Select(e => e.Id).ToList()).ConfigureAwait(false) ?? new Dictionary<int, List<ProductoRequeridoLeido>>();
                foreach (EventoDTO dto in lista)
                {
                    dto.ProductosRequeridos = requeridos.TryGetValue(dto.Id, out List<ProductoRequeridoLeido> suyos)
                        ? suyos.Select(r => new ProductoRequeridoEventoDTO { Producto = r.Producto?.Trim(), Nombre = r.Nombre?.Trim() }).ToList()
                        : new List<ProductoRequeridoEventoDTO>();
                }
                HashSet<int> exigenTodos = await ProductosRequeridos
                    .EventosQueExigenTodos(lista.Select(e => e.Id).ToList()).ConfigureAwait(false) ?? new HashSet<int>();
                foreach (EventoDTO dto in lista)
                {
                    dto.ProductosRequeridosModo = ReglasProductosRequeridosEvento.Modo(exigenTodos.Contains(dto.Id));
                }
            }
            catch (Exception ex)
            {
                if (System.Threading.Interlocked.Exchange(ref requeridosFalloRegistrado, 1) == 0)
                {
                    ElmahHelper.Log(new Exception($"[Eventos #732] No se han podido leer los productos requeridos de los eventos: {ex.Message}", ex));
                }
            }
            return lista;
        }

        private async Task<EventoDTO> ConProductosRequeridos(EventoDTO dto) =>
            dto == null ? null : (await ConProductosRequeridos(new List<EventoDTO> { dto }).ConfigureAwait(false)).Single();

        private static int pedidoMinimoFalloRegistrado;

        /// <summary>
        /// NestoAPI#613: el pedido mínimo del cupón de cada evento, en UNA consulta (0 si no lo tiene, sin el script o si falla; el
        /// fallo va a ELMAH una vez).
        /// </summary>
        private async Task ConPedidoMinimoCupon(List<EventoDTO> lista)
        {
            if (lista == null || !lista.Any())
            {
                return;
            }
            var minimos = new Dictionary<int, decimal>();
            if (PedidoMinimoCupon != null)
            {
                try
                {
                    minimos = await PedidoMinimoCupon.Leer(lista.Select(e => e.Id).ToList()).ConfigureAwait(false) ?? minimos;
                }
                catch (Exception ex)
                {
                    if (System.Threading.Interlocked.Exchange(ref pedidoMinimoFalloRegistrado, 1) == 0)
                    {
                        ElmahHelper.Log(new Exception($"[Eventos #613] No se ha podido leer el pedido mínimo del cupón de los eventos: {ex.Message}", ex));
                    }
                }
            }
            foreach (EventoDTO dto in lista)
            {
                dto.PedidoMinimoCupon = minimos.TryGetValue(dto.Id, out decimal minimo) ? minimo : 0;
            }
        }

        public const string MENSAJE_FALTA_SCRIPT_PEDIDO_MINIMO = "Todavía no se puede poner un pedido mínimo para usar el cupón: falta lanzar " +
            "el script Scripts/Issue613_CuponPedidoMinimoPorEvento.sql. Avise a Informática.";

        /// <summary>Lo que cabe en decimal(10, 2).</summary>
        private const decimal IMPORTE_MAXIMO_PEDIDO_MINIMO = 99999999.99M;

        /// <summary>NestoAPI#613: el pedido mínimo del cupón que viene en el cuerpo, redondeado. Null si no viene (no se toca). Negativo, 400.</summary>
        private static decimal? ValidarPedidoMinimoCupon(EventoDTO evento)
        {
            if (evento?.PedidoMinimoCupon == null)
            {
                return null;
            }
            if (evento.PedidoMinimoCupon.Value < 0)
            {
                throw new NestoBusinessException("El pedido mínimo para usar el cupón no puede ser negativo (0 = cualquier importe).");
            }
            if (evento.PedidoMinimoCupon.Value > IMPORTE_MAXIMO_PEDIDO_MINIMO)
            {
                throw new NestoBusinessException("El pedido mínimo para usar el cupón es demasiado grande.");
            }
            return Math.Round(evento.PedidoMinimoCupon.Value, 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// NestoAPI#613: guarda el pedido mínimo si viene. Sin la columna (o sin repositorio), 0 no cambia nada (es lo de siempre) y
        /// más de 0 contesta que falta el script.
        /// </summary>
        private async Task GuardarPedidoMinimoCupon(int eventoId, decimal? minimo)
        {
            if (minimo == null)
            {
                return;
            }
            bool guardado = PedidoMinimoCupon != null && await PedidoMinimoCupon.Guardar(eventoId, minimo.Value).ConfigureAwait(false);
            if (!guardado && minimo.Value > 0)
            {
                throw new NestoBusinessException(MENSAJE_FALTA_SCRIPT_PEDIDO_MINIMO);
            }
        }

        /// <summary>
        /// NestoAPI#732: los productos requeridos que vienen en el cuerpo, comprobados (existen en la empresa del evento). Null si
        /// no vienen (no se tocan). 400 si alguno no existe o si todavía no se ha lanzado el script.
        /// </summary>
        private async Task<List<string>> ValidarProductosRequeridos(EventoDTO evento, string empresa)
        {
            if (evento == null || !evento.TraeProductosRequeridos())
            {
                return null;
            }
            if (ProductosRequeridos == null)
            {
                throw new NestoBusinessException(MENSAJE_FALTA_SCRIPT_REQUERIDOS);
            }
            List<string> productos = ReglasProductosRequeridosEvento.Normalizar(evento.ProductosRequeridos);
            string largo = productos.FirstOrDefault(p => p.Length > LONGITUD_PRODUCTO);
            if (largo != null)
            {
                throw new NestoBusinessException($"La referencia {largo} no puede tener más de {LONGITUD_PRODUCTO} caracteres.");
            }
            if (productos.Any())
            {
                List<string> existen = (await db.Productos.Where(p => p.Empresa == empresa && productos.Contains(p.Número))
                    .Select(p => p.Número).ToListAsync().ConfigureAwait(false))
                    .Select(p => p?.Trim()).ToList();
                List<string> noExisten = productos.Where(p => !existen.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
                if (noExisten.Any())
                {
                    throw new NestoBusinessException(noExisten.Count == 1
                        ? $"No existe el producto {noExisten[0]}: revise los productos que hay que comprar para apuntarse."
                        : $"No existen los productos {string.Join(", ", noExisten)}: revise los productos que hay que comprar para apuntarse.");
                }
            }
            return productos;
        }

        public const string MENSAJE_FALTA_SCRIPT_REQUERIDOS = "Todavía no se pueden poner productos requeridos: falta lanzar el script " +
            "Scripts/Issue732_EventosProductosRequeridos.sql. Avise a Informática.";

        public const string MENSAJE_FALTA_SCRIPT_MODO = "Todavía no se puede exigir que se compren todos los productos requeridos: falta " +
            "lanzar el script Scripts/Issue732_EventosProductosRequeridosModo.sql. Avise a Informática.";

        /// <summary>
        /// NestoAPI#732: el modo que viene en el cuerpo («Alguno» o «Todos»), canónico. Null si no viene (no se toca). 400 de usted
        /// si es otro valor.
        /// </summary>
        private static string ValidarModoProductosRequeridos(EventoDTO evento)
        {
            if (evento == null || !evento.TraeProductosRequeridosModo())
            {
                return null;
            }
            return ReglasProductosRequeridosEvento.NormalizarModo(evento.ProductosRequeridosModo)
                ?? throw new NestoBusinessException($"El modo de los productos requeridos «{evento.ProductosRequeridosModo?.Trim()}» no es válido: " +
                    $"tiene que ser «{ReglasProductosRequeridosEvento.MODO_ALGUNO}» (basta con uno) o «{ReglasProductosRequeridosEvento.MODO_TODOS}» (hay que comprarlos todos).");
        }

        /// <summary>
        /// NestoAPI#732: guarda el modo si viene. Sin la columna (o sin repositorio), «Alguno» no cambia nada (es lo de siempre) y
        /// «Todos» contesta que falta el script.
        /// </summary>
        private async Task GuardarModoProductosRequeridos(int eventoId, string modo)
        {
            if (modo == null)
            {
                return;
            }
            bool todos = modo == ReglasProductosRequeridosEvento.MODO_TODOS;
            bool guardado = ProductosRequeridos != null && await ProductosRequeridos.GuardarModo(eventoId, todos).ConfigureAwait(false);
            if (!guardado && todos)
            {
                throw new NestoBusinessException(MENSAJE_FALTA_SCRIPT_MODO);
            }
        }

        private async Task GuardarProductosRequeridos(int eventoId, List<string> productos, string usuario)
        {
            if (productos == null)
            {
                return;
            }
            if (!await ProductosRequeridos.Guardar(eventoId, productos, UsuarioAuditoriaHelper.ParaAuditoria(usuario)).ConfigureAwait(false))
            {
                throw new NestoBusinessException(MENSAJE_FALTA_SCRIPT_REQUERIDOS);
            }
        }

        public async Task<EventoDTO> LeerEvento(int id)
        {
            Evento evento = await db.Eventos.SingleOrDefaultAsync(e => e.Id == id).ConfigureAwait(false);
            if (evento == null)
            {
                return null;
            }
            Dictionary<int, int> ocupadas = await PlazasOcupadas(new List<int> { id }).ConfigureAwait(false);
            EventoDTO dto = ADTO(evento, ocupadas.TryGetValue(id, out int n) ? n : 0);
            await ConPlazasSinPagar(new List<EventoDTO> { dto }).ConfigureAwait(false);
            return await ConProductosRequeridos(await ConImagen(dto).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private static int anunciosFalloRegistrado;

        /// <summary>
        /// NestoAPI#692: cuándo se anunció cada evento, en UNA consulta. Si falla (no se ha lanzado Issue692_AnunciarEventos.sql),
        /// salen como no anunciados y el fallo va a ELMAH una sola vez.
        /// </summary>
        private async Task<Dictionary<int, AnuncioEventoLeido>> Anunciados(List<int> ids)
        {
            if (anuncios == null || !ids.Any())
            {
                return new Dictionary<int, AnuncioEventoLeido>();
            }
            try
            {
                return await anuncios.LeerAnunciados(ids).ConfigureAwait(false) ?? new Dictionary<int, AnuncioEventoLeido>();
            }
            catch (Exception ex)
            {
                if (System.Threading.Interlocked.Exchange(ref anunciosFalloRegistrado, 1) == 0)
                {
                    ElmahHelper.Log(new Exception($"[Eventos #692] No se ha podido leer cuándo se anunciaron los eventos: {ex.Message}", ex));
                }
                return new Dictionary<int, AnuncioEventoLeido>();
            }
        }

        /// <summary>NestoAPI#692: el último anuncio y, con la imagen ya puesta, si está completo y qué le falta.</summary>
        private EventoDTO ConAnuncio(EventoDTO dto, Dictionary<int, AnuncioEventoLeido> anunciados)
        {
            if (anunciados != null && anunciados.TryGetValue(dto.Id, out AnuncioEventoLeido anuncio))
            {
                dto.AnunciadoFecha = anuncio.Fecha;
                dto.AnunciadoUsuario = anuncio.Usuario?.Trim();
            }
            return ReglasAnuncioEvento.Completar(dto, reloj().Date);
        }

        private static int imagenesFalloRegistrado;

        /// <summary>
        /// NestoAPI#690: qué eventos tienen imagen (y de cuándo es), en UNA consulta y sin el contenido. Si falla (la tabla
        /// todavía no existe, la BD…), los eventos salen igual sin imagen y el fallo va a ELMAH una sola vez.
        /// </summary>
        private async Task<Dictionary<int, DateTime>> FechasImagenes(List<int> ids)
        {
            if (imagenes == null || !ids.Any())
            {
                return new Dictionary<int, DateTime>();
            }
            try
            {
                return await imagenes.LeerFechas(ids).ConfigureAwait(false) ?? new Dictionary<int, DateTime>();
            }
            catch (Exception ex)
            {
                if (System.Threading.Interlocked.Exchange(ref imagenesFalloRegistrado, 1) == 0)
                {
                    ElmahHelper.Log(new Exception($"[Eventos #690] No se han podido leer las imágenes de los eventos; salen sin ellas: {ex.Message}", ex));
                }
                return new Dictionary<int, DateTime>();
            }
        }

        private static EventoDTO ConImagen(EventoDTO dto, Dictionary<int, DateTime> conImagen)
        {
            if (conImagen != null && conImagen.TryGetValue(dto.Id, out DateTime fecha))
            {
                dto.ImagenUrl = RepositorioImagenesEventos.Url(dto.Id, fecha);
            }
            return dto;
        }

        /// <summary>Un evento suelto: su imagen y (NestoAPI#692) su anuncio, si está completo y qué le falta.</summary>
        private async Task<EventoDTO> ConImagen(EventoDTO dto) =>
            ConAnuncio(ConImagen(dto, await FechasImagenes(new List<int> { dto.Id }).ConfigureAwait(false)),
                await Anunciados(new List<int> { dto.Id }).ConfigureAwait(false));

        // ---------------- NestoAPI#690: imagen ----------------

        public async Task<EventoDTO> GuardarImagen(int id, ImagenEventoAGrabar imagen, string usuario)
        {
            if (imagenes == null)
            {
                throw new NestoBusinessException("Las imágenes de los eventos no están disponibles.");
            }
            Evento evento = await db.Eventos.SingleOrDefaultAsync(e => e.Id == id).ConfigureAwait(false)
                ?? throw NoEncontrado($"No existe el evento {id}.");
            DateTime fecha = await imagenes.Guardar(id, imagen, UsuarioAuditoriaHelper.ParaAuditoria(usuario)).ConfigureAwait(false);
            Dictionary<int, int> ocupadas = await PlazasOcupadas(new List<int> { id }).ConfigureAwait(false);
            EventoDTO dto = ADTO(evento, ocupadas.TryGetValue(id, out int n) ? n : 0);
            dto.ImagenUrl = RepositorioImagenesEventos.Url(id, fecha);
            await ConPedidoMinimoCupon(new List<EventoDTO> { dto }).ConfigureAwait(false);
            return ConAnuncio(dto, await Anunciados(new List<int> { id }).ConfigureAwait(false));
        }

        public async Task<bool> BorrarImagen(int id)
        {
            if (imagenes == null)
            {
                return false;
            }
            return await imagenes.Borrar(id).ConfigureAwait(false);
        }

        public async Task<ImagenEvento> LeerImagen(int id) =>
            imagenes == null ? null : await imagenes.Leer(id).ConfigureAwait(false);

        /// <summary>NestoAPI#746: cuántas de las que ocupan plaza son reservas sin pagar (solo eventos con plazas).</summary>
        private async Task ConPlazasSinPagar(List<EventoDTO> lista)
        {
            List<int> ids = lista.Where(e => e.Plazas.HasValue && e.PlazasOcupadas > 0).Select(e => e.Id).ToList();
            if (!ids.Any())
            {
                return;
            }
            string reservada = EstadoInscripcionEvento.Reservada.ToString();
            var filas = await db.EventosInscripciones
                .Where(i => ids.Contains(i.EventoId) && i.Estado == reservada)
                .Select(i => new { i.EventoId, i.Estado, i.AsistenciaRespuesta })
                .ToListAsync().ConfigureAwait(false);
            Dictionary<int, int> sinPagar = filas
                .Where(f => ReglasPlazasLlenas.EsReservaSinPagar(new EventoInscripcion { Estado = f.Estado, AsistenciaRespuesta = f.AsistenciaRespuesta }))
                .GroupBy(f => f.EventoId).ToDictionary(g => g.Key, g => g.Count());
            foreach (EventoDTO dto in lista)
            {
                dto.PlazasSinPagar = sinPagar.TryGetValue(dto.Id, out int n) ? n : 0;
            }
        }

        /// <summary>NestoAPI#611: cuántas inscripciones ocupan plaza en cada evento.</summary>
        private async Task<Dictionary<int, int>> PlazasOcupadas(List<int> ids)
        {
            if (!ids.Any())
            {
                return new Dictionary<int, int>();
            }
            List<string> estados = ReglasInscripcionEvento.OCUPAN_PLAZA.Select(e => e.ToString()).ToList();
            // Carlos (22/10/26): «No podré ir» libera la plaza (ReglasInscripcionEvento.OcupaPlaza)
            var filas = await db.EventosInscripciones
                .Where(i => ids.Contains(i.EventoId) && estados.Contains(i.Estado))
                .Select(i => new { i.EventoId, i.Estado, i.AsistenciaRespuesta })
                .ToListAsync().ConfigureAwait(false);
            return filas
                .Where(f => ReglasInscripcionEvento.OcupaPlaza(new EventoInscripcion { Estado = f.Estado, AsistenciaRespuesta = f.AsistenciaRespuesta }))
                .GroupBy(f => f.EventoId).ToDictionary(g => g.Key, g => g.Count());
        }

        public async Task<EventoDTO> CrearEvento(EventoDTO evento, string usuario)
        {
            // NestoAPI#690 (Carlos, 22/10/26): sin importes en el cuerpo, 100 € reembolsable y 60 € no reembolsable. Un 0
            // explícito se respeta: con los dos a 0, el evento es gratuito.
            if (evento != null && !evento.Trae(nameof(EventoDTO.ImporteSenal)))
            {
                evento.ImporteSenal = ReglasModalidadEvento.SENAL_REEMBOLSABLE_POR_DEFECTO;
            }
            if (evento != null && !evento.Trae(nameof(EventoDTO.ImporteSenalNoReembolsable)))
            {
                evento.ImporteSenalNoReembolsable = ReglasModalidadEvento.SENAL_NO_REEMBOLSABLE_POR_DEFECTO;
            }
            await Validar(evento, null).ConfigureAwait(false);
            List<string> requeridos = await ValidarProductosRequeridos(evento, EmpresaOPorDefecto(evento.Empresa)).ConfigureAwait(false);
            string modoRequeridos = ValidarModoProductosRequeridos(evento);
            decimal? pedidoMinimoCupon = ValidarPedidoMinimoCupon(evento);
            var entidad = new Evento { Empresa = EmpresaOPorDefecto(evento.Empresa), Formato = FormatosEvento.PRESENCIAL };
            Copiar(evento, entidad, usuario);
            db.Eventos.Add(entidad);
            await db.SaveChangesAsync().ConfigureAwait(false);
            await GuardarProductosRequeridos(entidad.Id, requeridos, usuario).ConfigureAwait(false);
            await GuardarModoProductosRequeridos(entidad.Id, modoRequeridos).ConfigureAwait(false);
            await GuardarPedidoMinimoCupon(entidad.Id, pedidoMinimoCupon).ConfigureAwait(false);
            return await ConProductosRequeridos(ConAnuncio(ADTO(entidad, 0), null)).ConfigureAwait(false);
        }

        public async Task<EventoDTO> ModificarEvento(int id, EventoDTO evento, string usuario)
        {
            Evento entidad = await db.Eventos.SingleOrDefaultAsync(e => e.Id == id).ConfigureAwait(false)
                ?? throw NoEncontrado($"No existe el evento {id}.");
            if (evento != null)
            {
                evento.Empresa = Limpiar(entidad.Empresa);
            }
            await Validar(evento, entidad.Almacen).ConfigureAwait(false);
            List<string> requeridos = await ValidarProductosRequeridos(evento, EmpresaOPorDefecto(entidad.Empresa)).ConfigureAwait(false);
            string modoRequeridos = ValidarModoProductosRequeridos(evento);
            decimal? pedidoMinimoCupon = ValidarPedidoMinimoCupon(evento);
            Dictionary<int, int> ocupadas = await PlazasOcupadas(new List<int> { id }).ConfigureAwait(false);
            int ocupadasEvento = ocupadas.TryGetValue(id, out int n) ? n : 0;
            if (evento.Trae(nameof(EventoDTO.Plazas)) && evento.Plazas.HasValue && evento.Plazas.Value < ocupadasEvento)
            {
                throw new NestoBusinessException(
                    $"Ya hay {ocupadasEvento} inscripciones: no se pueden dejar {evento.Plazas.Value} plazas. Anule antes las que sobren.");
            }
            bool eraGratuito = ReglasModalidadEvento.EsGratuito(entidad);
            Copiar(evento, entidad, usuario);
            // NestoAPI#690: las inscripciones gratuitas no tienen pago y las de pago no son gratuitas: con inscripciones vivas,
            // el evento no cambia de gratuito a de pago ni al revés.
            if (eraGratuito != ReglasModalidadEvento.EsGratuito(entidad) && ocupadasEvento > 0)
            {
                throw new NestoBusinessException(eraGratuito
                    ? $"Ya hay {ocupadasEvento} inscripciones gratuitas: el evento no puede pasar a ser de pago. Anúlelas antes o cree otro evento."
                    : $"Ya hay {ocupadasEvento} inscripciones de pago: el evento no puede pasar a ser gratuito. Anúlelas antes o cree otro evento.");
            }
            await db.SaveChangesAsync().ConfigureAwait(false);
            await GuardarProductosRequeridos(id, requeridos, usuario).ConfigureAwait(false);
            await GuardarModoProductosRequeridos(id, modoRequeridos).ConfigureAwait(false);
            await GuardarPedidoMinimoCupon(id, pedidoMinimoCupon).ConfigureAwait(false);
            return await ConProductosRequeridos(await ConImagen(ADTO(entidad, ocupadasEvento)).ConfigureAwait(false)).ConfigureAwait(false);
        }

        /// <param name="almacenGuardado">Al modificar, la tienda que ya tiene el evento (si el cuerpo no trae Almacen, se queda esa).</param>
        private async Task Validar(EventoDTO evento, string almacenGuardado)
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
            // NestoAPI#690: la señal no reembolsable, el horario y el ponente
            if (evento.ImporteSenalNoReembolsable < 0)
            {
                throw new NestoBusinessException("El importe de la señal no reembolsable no puede ser negativo.");
            }
            TimeSpan? inicio = ReglasModalidadEvento.LeerHora(evento.HoraInicio, "inicio");
            TimeSpan? fin = ReglasModalidadEvento.LeerHora(evento.HoraFin, "fin");
            if (inicio.HasValue && fin.HasValue && fin.Value <= inicio.Value)
            {
                throw new NestoBusinessException("La hora de fin tiene que ser posterior a la de inicio.");
            }
            if ((evento.Ponente?.Trim().Length ?? 0) > LONGITUD_PONENTE)
            {
                throw new NestoBusinessException($"El ponente no puede tener más de {LONGITUD_PONENTE} caracteres.");
            }
            if (evento.DiasPagoAntesEvento.HasValue && (evento.DiasPagoAntesEvento.Value < 0 || evento.DiasPagoAntesEvento.Value > MAXIMO_DIAS_PAGO))
            {
                throw new NestoBusinessException($"Los días para pagar antes del evento tienen que estar entre 0 y {MAXIMO_DIAS_PAGO} (o vacío: 7).");
            }
            // NestoAPI#611 (Carlos, 14/10/26): cada evento es de una tienda, que es quien lo mantiene. Carlos (22/10/26): SIEMPRE,
            // también los online (el ponente emite desde una de las dos tiendas), y el pedido del curso lleva su almacén y su
            // delegación. Al modificar sin Almacen en el cuerpo, vale la que ya tenía.
            string almacen = evento.Trae(nameof(EventoDTO.Almacen)) ? evento.Almacen : almacenGuardado;
            if (!PermisosEventos.EsAlmacenDeEventos(almacen))
            {
                throw new NestoBusinessException(MENSAJE_FALTA_TIENDA);
            }
            if (evento.Trae(nameof(EventoDTO.Formato)))
            {
                _ = FormatosEvento.Interpretar(evento.Formato);
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
                var ficha = await db.Productos.Where(p => p.Empresa == empresa && p.Número == producto)
                    .Select(p => new { p.Grupo }).FirstOrDefaultAsync().ConfigureAwait(false);
                if (ficha == null)
                {
                    throw new NestoBusinessException($"No existe el producto {producto}: dé de alta antes la referencia del curso.");
                }
                // Carlos (22/10/26): un producto forzado puede ser cualquier curso que exista (grupo CUR), compartido por varios eventos
                if (!string.Equals(ficha.Grupo?.Trim(), Constantes.Productos.GRUPO_CURSOS, StringComparison.OrdinalIgnoreCase))
                {
                    throw new NestoBusinessException($"El producto {producto} no es un curso (grupo {Constantes.Productos.GRUPO_CURSOS}): " +
                        "elija la referencia de un curso o cree una para este evento con «Producto del curso».");
                }
            }
        }

        private void Copiar(EventoDTO origen, Evento destino, string usuario)
        {
            destino.Titulo = origen.Titulo.Trim();
            destino.Fecha = origen.Fecha.Date;
            // NestoAPI#690: el Nesto de #591 siempre la manda; si un cliente nuevo no la manda en el PUT, se conserva.
            if (origen.Trae(nameof(EventoDTO.ImporteSenal)))
            {
                destino.ImporteSenal = Math.Round(origen.ImporteSenal, 2, MidpointRounding.AwayFromZero);
            }
            destino.Activo = origen.Activo;
            if (origen.Trae(nameof(EventoDTO.ImporteSenalNoReembolsable)))
            {
                destino.ImporteSenalNoReembolsable = Math.Round(origen.ImporteSenalNoReembolsable, 2, MidpointRounding.AwayFromZero);
            }
            if (origen.Trae(nameof(EventoDTO.HoraInicio)))
            {
                destino.HoraInicio = ReglasModalidadEvento.LeerHora(origen.HoraInicio, "inicio");
            }
            if (origen.Trae(nameof(EventoDTO.HoraFin)))
            {
                destino.HoraFin = ReglasModalidadEvento.LeerHora(origen.HoraFin, "fin");
            }
            if (origen.Trae(nameof(EventoDTO.Ponente)))
            {
                destino.Ponente = Limpiar(origen.Ponente);
            }
            if (origen.Trae(nameof(EventoDTO.DiasPagoAntesEvento)))
            {
                destino.DiasPagoAntesEvento = origen.DiasPagoAntesEvento;
            }
            if (origen.Trae(nameof(EventoDTO.Formato)))
            {
                destino.Formato = FormatosEvento.Interpretar(origen.Formato);
            }
            // Con lo conservado del PUT (por ejemplo, solo viene la hora de fin)
            if (destino.HoraInicio.HasValue && destino.HoraFin.HasValue && destino.HoraFin.Value <= destino.HoraInicio.Value)
            {
                throw new NestoBusinessException("La hora de fin tiene que ser posterior a la de inicio.");
            }
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

        // ---------------- Carlos (22/10/26, Nesto#517): producto del curso ----------------

        public async Task<ProductoCursoDTO> CrearProductoCurso(int id, PeticionProductoCursoDTO peticion, string usuario)
        {
            Evento evento = await db.Eventos.SingleOrDefaultAsync(e => e.Id == id).ConfigureAwait(false)
                ?? throw NoEncontrado($"No existe el evento {id}.");
            ModalidadInscripcionEvento modalidad = ReglasModalidadEvento.Interpretar(peticion?.Modalidad)
                ?? throw new NestoBusinessException("Indique la modalidad: Reembolsable o NoReembolsable.");
            if (modalidad == ModalidadInscripcionEvento.Gratuita)
            {
                throw new NestoBusinessException("Un evento gratuito no se factura: no necesita producto.");
            }
            decimal pvp = ReglasModalidadEvento.Importe(evento, modalidad);
            if (pvp <= 0)
            {
                throw new NestoBusinessException($"«{evento.Titulo?.Trim()}» no tiene señal {ReglasModalidadEvento.Texto(modalidad).ToLowerInvariant()}: " +
                    "póngale antes el importe.");
            }
            string empresa = EmpresaOPorDefecto(evento.Empresa);
            string producto = ReglasProductoCurso.Referencia(evento.Id, modalidad);
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            string nombre = await productosCurso.LeerNombre(empresa, producto).ConfigureAwait(false);
            bool creado = false;
            if (nombre == null)
            {
                string plantilla = ReglasProductoCurso.Plantilla;
                nombre = ReglasProductoCurso.Nombre(evento);
                string comentarios = $"Curso del evento {evento.Id} ({ReglasModalidadEvento.Texto(modalidad).ToLowerInvariant()}), " +
                    $"del {evento.Fecha:dd/MM/yyyy}. Copia de la ficha de {plantilla}.";
                int filas = await productosCurso.CrearDesdePlantilla(empresa, producto, plantilla, nombre, pvp, comentarios, usuarioAuditoria)
                    .ConfigureAwait(false);
                if (filas == 0)
                {
                    // O no está la plantilla, o alguien la acaba de crear a la vez
                    nombre = await productosCurso.LeerNombre(empresa, producto).ConfigureAwait(false)
                        ?? throw new NestoBusinessException($"No se ha podido crear {producto}: no existe el curso modelo {plantilla} en la empresa {empresa}.");
                }
                else
                {
                    creado = true;
                    // La factura del curso puede traspasarse a la empresa espejo: allí también, si está la plantilla
                    string espejo = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO;
                    if (espejo != empresa)
                    {
                        try
                        {
                            _ = await productosCurso.CrearDesdePlantilla(espejo, producto, plantilla, nombre, pvp, comentarios, usuarioAuditoria).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            ElmahHelper.Log(new Exception($"[Producto del curso] {producto} está creado en la empresa {empresa}, pero no en la {espejo}: {ex.Message}", ex));
                        }
                    }
                }
            }

            string queda = ReglasProductoCurso.ProductoDelEvento(evento, producto);
            if (!string.Equals(evento.ProductoCurso?.Trim(), queda, StringComparison.Ordinal))
            {
                evento.ProductoCurso = queda;
                evento.Usuario = usuarioAuditoria;
                evento.FechaModificacion = reloj();
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            return new ProductoCursoDTO { Producto = producto, Nombre = nombre?.Trim(), Creado = creado };
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
            FechaListaAsistencia = e.FechaListaAsistencia,
            HoraInicio = ReglasModalidadEvento.EscribirHora(e.HoraInicio),
            HoraFin = ReglasModalidadEvento.EscribirHora(e.HoraFin),
            Ponente = e.Ponente?.Trim(),
            ImporteSenalNoReembolsable = e.ImporteSenalNoReembolsable,
            DiasPagoAntesEvento = e.DiasPagoAntesEvento,
            Formato = FormatosEvento.De(e),
            Gratuito = ReglasModalidadEvento.EsGratuito(e),
            Modalidades = ReglasModalidadEvento.Disponibles(e).Select(m => m.ToString()).ToList()
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
                    $"El apunte {numOrden} ya es la señal del evento «{otro?.Titulo?.Trim() ?? yaMarcada.EventoId.ToString()}». Quite esa señal antes si se ha equivocado.");
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

        /// <summary>
        /// NestoAPI#612 (Carlos, 16/10/26): si la señal pagó alguna inscripción (sin factura del curso ni cupón pendientes), se
        /// suelta de la inscripción y se borra la señal, en una transacción (antes daba 500 por FK_EventosInscripciones_EventosSenales).
        /// </summary>
        public async Task QuitarSenal(int id, string usuario)
        {
            EventoSenal senal = await db.EventosSenales.SingleOrDefaultAsync(s => s.Id == id).ConfigureAwait(false)
                ?? throw NoEncontrado($"No existe la señal {id} (puede que ya la haya quitado otra persona).");
            List<EventoInscripcion> pagadas = await db.EventosInscripciones.Where(i => i.EventoSenalId == senal.Id)
                .ToListAsync().ConfigureAwait(false);
            await ComprobarQueNoEstaUsada(senal, pagadas).ConfigureAwait(false);
            if (!pagadas.Any())
            {
                db.EventosSenales.Remove(senal);
                await db.SaveChangesAsync().ConfigureAwait(false);
                return;
            }

            DateTime ahora = reloj();
            Dictionary<int, Evento> eventosInscripciones = await LeerEventosDe(pagadas).ConfigureAwait(false);
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            await enTransaccion(async () =>
            {
                foreach (EventoInscripcion inscripcion in pagadas)
                {
                    eventosInscripciones.TryGetValue(inscripcion.EventoId, out Evento evento);
                    SoltarSenal(inscripcion, evento, ahora, usuarioAuditoria);
                }
                // Dos SaveChanges: en el EDMX no hay asociación entre las dos tablas y EF no sabría que tiene que soltar antes de borrar.
                await db.SaveChangesAsync().ConfigureAwait(false);
                db.EventosSenales.Remove(senal);
                await db.SaveChangesAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        private async Task<Dictionary<int, Evento>> LeerEventosDe(List<EventoInscripcion> inscripciones)
        {
            List<int> ids = inscripciones.Select(i => i.EventoId).Distinct().ToList();
            return (await db.Eventos.Where(e => ids.Contains(e.Id)).ToListAsync().ConfigureAwait(false))
                .ToDictionary(e => e.Id);
        }

        /// <summary>
        /// Deshace lo que hizo el pago con la señal (ServicioInscripcionesEventos.Pagar con forma Transferencia): la inscripción
        /// vuelve a Reservada (pendiente de pago), sin forma, importe, fecha ni usuario del pago, y con una fecha límite nueva
        /// (7 días desde hoy, nunca después del evento). Si la pagó otra cosa (el enlace de tarjeta, efectivo) y la señal solo se
        /// le apuntó después, ese pago sigue valiendo: solo se suelta la señal.
        /// </summary>
        internal static void SoltarSenal(EventoInscripcion inscripcion, Evento evento, DateTime ahora, string usuario)
        {
            inscripcion.EventoSenalId = null;
            if (ReglasInscripcionEvento.FormaPago(inscripcion.FormaPago) == FormaPagoInscripcionEvento.Transferencia)
            {
                inscripcion.Estado = EstadoInscripcionEvento.Reservada.ToString();
                inscripcion.FormaPago = null;
                inscripcion.ImportePagado = null;
                inscripcion.FechaPago = null;
                inscripcion.UsuarioPago = null;
                inscripcion.FechaLimitePago = ReglasInscripcionEvento.FechaLimitePago(ahora, evento?.Fecha ?? DateTime.MaxValue);
            }
            inscripcion.Usuario = usuario;
            inscripcion.FechaModificacion = ahora;
        }

        /// <summary>
        /// NestoAPI#612 (Carlos, 16/10/26): una señal que ya ha servido para facturar el curso (y emitir el cupón) no se quita
        /// hasta que la factura del curso tenga rectificativa y el cupón esté anulado: 409 <see cref="ERROR_SENAL_YA_USADA"/>
        /// diciendo qué falta. Si el cupón ya se usó, no se puede. Con las dos cosas hechas (o sin factura ni cupón), se suelta y se borra.
        /// </summary>
        private async Task ComprobarQueNoEstaUsada(EventoSenal senal, List<EventoInscripcion> pagadas)
        {
            foreach (int inscripcionId in pagadas.Select(i => i.Id))
            {
                FacturacionCursoInscripcion facturacion = await facturacionCursos.Leer(inscripcionId).ConfigureAwait(false);
                CuponLeido cupon = await cupones.LeerDeInscripcion(inscripcionId).ConfigureAwait(false);
                string factura = Limpiar(facturacion?.Factura);
                bool rectificada = factura != null
                    && await TieneRectificativa(facturacion.EmpresaFactura ?? senal.Empresa, factura).ConfigureAwait(false);
                string mensaje = MensajeSenalUsada(factura, rectificada, cupon);
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

        /// <summary>
        /// NestoAPI#612 (Carlos, 16/10/26): la factura del curso ya tiene rectificativa. Mismo criterio que
        /// ServicioFacturasPendientesVerifactu: hay alguna fila en LinFacturaVtaRectificacion cuya FacturaOriginalNumero es esta
        /// factura (Verifactu #37/#38: la rellenan CopiarFactura, la facturación del pedido rectificativo y el auto-curado LIFO).
        /// LinFacturaVtaRectificacion.Empresa es char(1): se compara la empresa sin relleno.
        /// </summary>
        private async Task<bool> TieneRectificativa(string empresa, string factura)
        {
            string empresaLimpia = Limpiar(empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string numero = Limpiar(factura);
            return await db.LinFacturaVtaRectificaciones
                .AnyAsync(r => r.Empresa == empresaLimpia && r.FacturaOriginalNumero.Trim() == numero).ConfigureAwait(false);
        }

        /// <summary>
        /// Null si la señal se puede quitar: la factura del curso, si la hay, ya tiene rectificativa, y el cupón, si lo hay, está
        /// anulado (NestoAPI#612, Carlos 16/10/26). Si no, dice exactamente qué falta; si el cupón ya se usó, que no se puede.
        /// </summary>
        internal static string MensajeSenalUsada(string factura, bool facturaRectificada, CuponLeido cupon)
        {
            string codigo = Limpiar(cupon?.Codigo);
            string estado = cupon?.Estado?.Trim();
            bool anulado = codigo != null && estado == EstadosCupon.ANULADO;
            bool usado = codigo != null && estado == EstadosCupon.USADO;
            bool faltaRectificativa = factura != null && !facturaRectificada;
            bool faltaAnularCupon = codigo != null && !anulado;
            if (!faltaRectificativa && !faltaAnularCupon)
            {
                return null;
            }

            var usos = new List<string>();
            if (factura != null)
            {
                usos.Add(facturaRectificada
                    ? $"facturar el curso (factura {factura}, que ya tiene rectificativa)"
                    : $"facturar el curso (factura {factura})");
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
                return mensaje + $" El cupón ya se ha usado{dondeSeUso}, así que no se puede anular y la señal no se puede quitar.";
            }

            var pasos = new List<string>();
            if (faltaRectificativa)
            {
                pasos.Add("haga primero la rectificativa del curso");
            }
            if (faltaAnularCupon)
            {
                pasos.Add(faltaRectificativa ? "anule el cupón" : "anule primero el cupón");
            }
            return mensaje + " Para anularla, " + string.Join(" y ", pasos) + ".";
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
