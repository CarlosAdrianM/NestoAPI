using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Pagos.PagoAPlazos;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#864: el pago a plazos de un pedido que ya existe. Lo pasa un empleado (Nesto) o un vendedor (NestoApp) y el
    /// cliente termina la financiación en la app (push) o con un enlace. Rutas y DTO NEUTROS: la financiera es un dato de la
    /// respuesta (<c>Proveedor</c> / <c>NombreProveedor</c>), no parte del contrato. Controlador propio para no engordar
    /// PedidosVentaController ni PedidosClienteController. Los endpoints de la app de #663 (api/Pedidos/Cliente/…/Aplazame)
    /// no cambian.
    /// </summary>
    [Authorize]
    public class PagoAPlazosController : ApiController
    {
        private readonly IServicioPagoAPlazos servicio;

        public PagoAPlazosController() : this(ServicioPagoAPlazos.CrearPorDefecto())
        {
        }

        public PagoAPlazosController(IServicioPagoAPlazos servicio)
        {
            this.servicio = servicio;
        }

        /// <summary>«El cliente es de este vendedor» (empresa, usuario, vendedor, cliente). Sustituible en tests.</summary>
        internal Func<string, string, string, string, bool> EsClienteDelVendedor { get; set; } = ClientesDelVendedor.EsDelVendedor;

        // GET api/PedidosVenta/1/930500/PagoAPlazos/Disponible
        /// <summary>
        /// Si a ESTE pedido se le puede ofrecer el pago a plazos y por qué canal. Siempre 200 con <c>Disponible</c> y, si no,
        /// el <c>Motivo</c> (de usted): interruptor apagado, importe fuera de los límites del proveedor, pedido ya pagado,
        /// facturado o financiado, o que no puede salir dentro del plazo de captura. 403 sin permiso, 404 si el pedido no
        /// existe, 503 sin el script.
        /// </summary>
        [HttpGet]
        [Route("api/PedidosVenta/{empresa}/{pedido:int}/PagoAPlazos/Disponible")]
        [ResponseType(typeof(DisponibilidadPagoAPlazosDTO))]
        public async Task<IHttpActionResult> GetDisponible(string empresa, int pedido)
        {
            IHttpActionResult corte = await Corte(empresa, pedido).ConfigureAwait(false);
            return corte ?? Responder(await servicio.Disponible(empresa, pedido).ConfigureAwait(false));
        }

        // POST api/PedidosVenta/1/930500/PagoAPlazos   { "Canal": "App" | "Enlace" | null, "Correo": null }
        /// <summary>
        /// Abre la solicitud de pago a plazos del pedido por lo que le queda por pagar. Canal App: push al cliente; Canal
        /// Enlace: devuelve <c>UrlPago</c> para mandársela; sin canal: App si el cliente la tiene y, si no, Enlace. 400 canal no
        /// válido o sin correo del cliente; 403; 404; 409 si no está disponible (con el motivo) o si pide App y el cliente no la
        /// tiene; 502 si el proveedor no deja abrirla; 503 sin el script.
        /// </summary>
        [HttpPost]
        [Route("api/PedidosVenta/{empresa}/{pedido:int}/PagoAPlazos")]
        [ResponseType(typeof(SolicitudPagoAPlazosDTO))]
        public async Task<IHttpActionResult> PostSolicitud(string empresa, int pedido, [FromBody] PeticionPagoAPlazosDTO peticion)
        {
            IHttpActionResult corte = await Corte(empresa, pedido).ConfigureAwait(false);
            return corte ?? Responder(await servicio.Solicitar(empresa, pedido, peticion, UsuarioAuditoriaHelper.Resolver(User, null)).ConfigureAwait(false));
        }

        // GET api/PedidosVenta/1/930500/PagoAPlazos/Estado
        /// <summary>
        /// Cómo va el pago a plazos del pedido: SinSolicitud, Pendiente, Aprobado, Capturado, Cancelado, Caducado o Rechazado, con
        /// sus fechas e importes. 403, 404 si el pedido no existe, 503 sin el script.
        /// </summary>
        [HttpGet]
        [Route("api/PedidosVenta/{empresa}/{pedido:int}/PagoAPlazos/Estado")]
        [ResponseType(typeof(EstadoPagoAPlazosDTO))]
        public async Task<IHttpActionResult> GetEstado(string empresa, int pedido)
        {
            IHttpActionResult corte = await Corte(empresa, pedido).ConfigureAwait(false);
            return corte ?? Responder(await servicio.Estado(empresa, pedido).ConfigureAwait(false));
        }

        // DELETE api/PedidosVenta/1/930500/PagoAPlazos?motivo=El cliente prefiere tarjeta
        /// <summary>
        /// Cancela el pago a plazos vivo del pedido: pendiente (el cliente ya no podrá terminarlo) o aprobado y sin cobrar (se
        /// cancela en el proveedor y se quita el prepago). 403, 404, 409 si no hay nada que cancelar o ya está cobrado, 502 si
        /// el proveedor no deja cancelarlo, 503 sin el script.
        /// </summary>
        [HttpDelete]
        [Route("api/PedidosVenta/{empresa}/{pedido:int}/PagoAPlazos")]
        [ResponseType(typeof(EstadoPagoAPlazosDTO))]
        public async Task<IHttpActionResult> DeleteSolicitud(string empresa, int pedido, string motivo = null)
        {
            IHttpActionResult corte = await Corte(empresa, pedido).ConfigureAwait(false);
            return corte ?? Responder(await servicio.Cancelar(empresa, pedido, motivo, UsuarioAuditoriaHelper.Resolver(User, null)).ConfigureAwait(false));
        }

        // GET api/Pedidos/Cliente/930500/PagoAPlazos
        /// <summary>
        /// Para la APP DE CLIENTES (token del cliente): el pago a plazos de SU pedido, con la <c>UrlPago</c> que tiene que abrir
        /// en el WebView si sigue pendiente. Es a donde lleva la push «PagoAPlazosPendiente». 401 si no es un cliente, 404 si el
        /// pedido no es suyo o no tiene solicitud (la misma respuesta), 503 sin el script.
        /// </summary>
        [HttpGet]
        [Route("api/Pedidos/Cliente/{pedido:int}/PagoAPlazos")]
        [ResponseType(typeof(PagoAPlazosClienteDTO))]
        public async Task<IHttpActionResult> GetParaElCliente(int pedido)
        {
            ClaimsIdentity identity = User?.Identity as ClaimsIdentity;
            string cliente = identity?.FindFirst("cliente")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(cliente) || !ValidadorAccesoCliente.ValidarAcceso(identity, cliente).Autorizado)
            {
                return Unauthorized();
            }
            return Responder(await servicio.ParaElCliente(Constantes.Empresas.EMPRESA_POR_DEFECTO, cliente, pedido).ConfigureAwait(false));
        }

        // POST api/PagoAPlazos/Notificacion/{proveedor}
        /// <summary>
        /// La notificación del proveedor (aprobación, rechazo…). <b>Sin JWT</b>: quien llama es la financiera, que se identifica a
        /// su manera (la valida su <see cref="IProveedorPagoAPlazos"/>; sin ella, no se toca nada). El código y el cuerpo de la
        /// respuesta los pone también el proveedor.
        /// </summary>
        [HttpPost]
        [Route("api/PagoAPlazos/Notificacion/{proveedor}")]
        [AllowAnonymous]
        public async Task<HttpResponseMessage> Notificacion(string proveedor)
        {
            string autorizacion = CabeceraAutorizacion(Request);
            string cuerpo = Request.Content == null ? null : await Request.Content.ReadAsStringAsync().ConfigureAwait(false);

            RespuestaNotificacionProveedor respuesta;
            try
            {
                respuesta = await servicio.ProcesarNotificacion(proveedor, autorizacion, cuerpo).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Un 500 hace que el proveedor reintente, que es lo que se quiere si algo ha fallado de verdad aquí
                ElmahHelper.Log(new Exception($"[Pago a plazos #864] Error al procesar la notificación de {proveedor}: {ex.Message}", ex));
                return Request.CreateResponse(HttpStatusCode.InternalServerError);
            }

            HttpResponseMessage http = Request.CreateResponse((HttpStatusCode)respuesta.CodigoHttp);
            if (respuesta.Cuerpo != null)
            {
                http.Content = new StringContent(respuesta.Cuerpo, Encoding.UTF8, respuesta.TipoContenido ?? "application/json");
            }
            return http;
        }

        /// <summary>La cabecera Authorization tal cual llegó («Bearer xxx»). Internal para los tests.</summary>
        internal static string CabeceraAutorizacion(HttpRequestMessage request)
        {
            var autorizacion = request?.Headers?.Authorization;
            if (autorizacion == null)
            {
                return null;
            }
            return string.IsNullOrEmpty(autorizacion.Parameter) ? autorizacion.Scheme : autorizacion.Scheme + " " + autorizacion.Parameter;
        }

        /// <summary>
        /// Los permisos: 403 a un cliente de la tienda y a un vendedor de NestoApp con el pedido de un cliente que no es suyo
        /// (404 si el pedido no existe). Null = puede seguir.
        /// </summary>
        private async Task<IHttpActionResult> Corte(string empresa, int pedido)
        {
            if (!PermisosPagoAPlazos.EsDeLaCasa(User))
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            if (!PermisosPagoAPlazos.EsVendedor(User))
            {
                return null;
            }
            string cliente = await servicio.ClienteDelPedido(empresa, pedido).ConfigureAwait(false);
            if (cliente == null)
            {
                return Content(HttpStatusCode.NotFound, new { Message = $"No existe el pedido {pedido}." });
            }
            return PermisosPagoAPlazos.PuedeConElCliente(User, empresa, cliente, EsClienteDelVendedor)
                ? null
                : Content(HttpStatusCode.Forbidden, new { Message = "Solo puede ofrecer el pago a plazos en los pedidos de sus clientes." });
        }

        private IHttpActionResult Responder<T>(ResultadoPagoAPlazos<T> resultado)
        {
            if (resultado.EsOk)
            {
                return Ok(resultado.Contenido);
            }
            return Content(resultado.Codigo, new { Message = resultado.Mensaje });
        }
    }
}
