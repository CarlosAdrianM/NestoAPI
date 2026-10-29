using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#613 (cupones, corte 3 de #610): los cupones de descuento de los eventos. Consultar puede cualquiera de la casa y el
    /// cliente de la tienda online (solo los suyos); emitir uno sin evento, solo Dirección (403 si no). Los errores de negocio
    /// salen como 400 por el filtro global. Aplicarlo en un pedido va por api/PedidosVenta (línea con CodigoCupon).
    /// <para>NestoAPI#694: la credencial va en cada método. GET de los cupones del cliente, ComprobarCarrito y Usar admiten también
    /// la X-API-KEY de PrestaShop (módulo nestocheques, de servidor a servidor), como el cheque regalo (#652); sin ninguna de las
    /// dos, 401 siempre. [Authorize] en la clase no vale: se sumaría al atributo y la tienda (solo API key) no pasaría.</para>
    /// </summary>
    public class CuponesController : ApiController
    {
        private const string API_KEY_PRESTASHOP = "ApiKeyPrestashop";
        private const string CABECERA_API_KEY = "X-API-KEY";

        private readonly NVEntities db;
        private readonly IServicioCupones servicio;
        private IServicioCanjeCupones servicioCanje;

        /// <summary>NestoAPI#694: el canje (las reglas de POST api/PedidosVenta) para ComprobarCarrito. Inyectable en los tests.</summary>
        internal IServicioCanjeCupones ServicioCanje
        {
            get => servicioCanje ?? (servicioCanje = new ServicioCanjeCupones(new RepositorioCupones(db)));
            set => servicioCanje = value;
        }

        public CuponesController() : this(new NVEntities())
        {
        }

        internal CuponesController(NVEntities db, IServicioCupones servicio = null)
        {
            this.db = db;
            db.Configuration.LazyLoadingEnabled = false;
            this.servicio = servicio ?? new ServicioCupones(db);
        }

        // GET api/Clientes/1/15191/Cupones?todos=false
        /// <summary>
        /// Los cupones del cliente: por defecto, los que se pueden usar (Activo) y los que valdrán (Emitido), el que se puede
        /// usar primero; con todos=true, también los usados, anulados y caducados. Para que la plantilla avise «tiene un cupón».
        /// </summary>
        [HttpGet]
        [AutorizadoOApiKey(API_KEY_PRESTASHOP, CABECERA_API_KEY, ExigirSiempre = true)]
        [Route("api/Clientes/{empresa}/{cliente}/Cupones")]
        [ResponseType(typeof(List<CuponDTO>))]
        public async Task<IHttpActionResult> GetCuponesCliente(string empresa, string cliente, bool todos = false)
        {
            return Ok(await servicio.LeerDelCliente(empresa, cliente, todos, User).ConfigureAwait(false));
        }

        // GET api/Cupones/EV-2610-7K3Q
        /// <summary>Un cupón por su código (404 si no existe o, para un cliente de la tienda, si no es suyo).</summary>
        [HttpGet]
        [Authorize]
        [Route("api/Cupones/{codigo}")]
        [ResponseType(typeof(CuponDTO))]
        public async Task<IHttpActionResult> GetCupon(string codigo)
        {
            return Ok(await servicio.LeerPorCodigo(codigo, User).ConfigureAwait(false));
        }

        // POST api/Cupones   { Empresa, Cliente, Contacto?, Importe, Motivo, ValidoDesde?, PedidoMinimo? }
        /// <summary>
        /// Cupón sin evento: solo Dirección, con motivo obligatorio. Mismas reglas de uso; caduca a los 6 meses. PedidoMinimo: si no
        /// viene, 200 €; 0, cualquier importe; negativo, 400 (NestoAPI#613, 29/10/26).
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Cupones")]
        [ResponseType(typeof(CuponDTO))]
        public async Task<IHttpActionResult> PostCupon([FromBody] CrearCuponManualDTO peticion)
        {
            return Ok(await servicio.CrearManual(peticion, User).ConfigureAwait(false));
        }

        // POST api/Cupones/EV-2610-7K3Q/Anular   { Motivo? }
        /// <summary>
        /// Devolución del curso (la factura se rectifica a mano): anula el cupón si no se ha usado. Si ya se usó, 409 con el
        /// pedido donde se usó y no se toca. Solo Administración y Dirección.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Cupones/{codigo}/Anular")]
        [ResponseType(typeof(CuponDTO))]
        public async Task<IHttpActionResult> PostAnularCupon(string codigo, [FromBody] AnularCuponDTO peticion)
        {
            return Ok(await servicio.Anular(codigo, peticion, User).ConfigureAwait(false));
        }

        // POST api/Cupones/EV-2610-7K3Q/Usar   { Empresa, Pedido?, PedidoCanal?, ImporteDescontado?, Canal? }
        /// <summary>
        /// El cupón se ha gastado en la tienda PrestaShop. Resultado Usado o YaEnEstePedido (idempotente); si ya estaba usado en otro
        /// pedido o anulado, Conflicto (200): el pedido entra igual y se avisa a Administración. 404 si no existe el cupón; 403 para el
        /// cliente de la tienda con JWT.
        /// <para>NestoAPI#694, desde el módulo nestocheques (X-API-KEY de PrestaShop) al validar el pedido en la tienda: sin Pedido,
        /// con <c>PedidoCanal</c> = referencia del pedido de la tienda, <c>Canal = "PrestaShop"</c> e <c>ImporteDescontado</c>. Queda
        /// Usado sin pedido de Nesto hasta que CanalesExternos lo importa. No depende del interruptor del espejo.</para>
        /// <para>NestoAPI#614, desde Nesto al importar el pedido (CanalesExternos), con Pedido: con el interruptor
        /// Cupones:EspejoPrestashop apagado, 400, salvo que el módulo ya lo apuntara con esa misma referencia (YaEnEstePedido).</para>
        /// </summary>
        [HttpPost]
        [AutorizadoOApiKey(API_KEY_PRESTASHOP, CABECERA_API_KEY, ExigirSiempre = true)]
        [Route("api/Cupones/{codigo}/Usar")]
        [ResponseType(typeof(UsoCuponTiendaDTO))]
        public async Task<IHttpActionResult> PostUsarCupon(string codigo, [FromBody] UsarCuponTiendaDTO peticion)
        {
            return Ok(await servicio.UsarEnTienda(codigo, peticion, User, conApiKey: ConApiKey()).ConfigureAwait(false));
        }

        // POST api/Cupones/ComprobarCarrito   { empresa, cliente, codigo?, lineas: [{ producto, cantidad, baseImponible }] }
        /// <summary>
        /// NestoAPI#694: ¿puede el cliente usar su cupón con este carrito de la tienda PrestaShop (módulo nestocheques)? Sin código, el
        /// cupón del cliente que mejor encaja (<see cref="ReglasCupon.ElegirParaPedido"/>, la regla de Nesto). Las reglas son las de
        /// POST api/PedidosVenta (<see cref="IServicioCanjeCupones"/>): mínimo el doble del cupón IVA incluido, sin cursos ni portes.
        /// No reserva nada. 200 con <see cref="ComprobacionCarritoCuponDTO"/>; un JWT de cliente solo puede preguntar por el suyo (403).
        /// </summary>
        [HttpPost]
        [AutorizadoOApiKey(API_KEY_PRESTASHOP, CABECERA_API_KEY, ExigirSiempre = true)]
        [Route("api/Cupones/ComprobarCarrito")]
        [ResponseType(typeof(ComprobacionCarritoCuponDTO))]
        public async Task<IHttpActionResult> ComprobarCarrito([FromBody] ComprobarCarritoCuponDTO carrito)
        {
            if (carrito == null || string.IsNullOrWhiteSpace(carrito.Cliente))
            {
                return BadRequest("Falta el cliente");
            }
            string clienteDelToken = (User?.Identity as ClaimsIdentity)?.FindFirst("cliente")?.Value;
            if (clienteDelToken != null && clienteDelToken.Trim() != carrito.Cliente.Trim())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            return Ok(await ServicioCanje.ComprobarCarritoTienda(carrito).ConfigureAwait(false));
        }

        /// <summary>El filtro ya ha dejado pasar la petición: sin JWT, es que traía la X-API-KEY de PrestaShop.</summary>
        private bool ConApiKey() => User?.Identity?.IsAuthenticated != true;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
