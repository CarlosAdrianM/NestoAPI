using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.EnlacesDescarga;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#737 (sugerencias 866/867, Paloma; 831, María José): el enlace de descarga del pedido o de la factura para mandarlo por
    /// WhatsApp Web desde Nesto (Nesto#570). Crearlo es solo para los empleados (token de Windows de Nesto, claim IsEmployee), como
    /// el envío por correo (#724); descargarlo (<see cref="DescargaDocumentoController"/>), para cualquiera que tenga el enlace.
    /// </summary>
    [Authorize]
    public class DocumentosController : ApiController
    {
        internal const string MOTIVO_SOLO_EMPLEADOS = "Solo los empleados pueden crear enlaces de descarga.";

        private readonly GestorEnlacesDescarga gestor;

        public DocumentosController() : this(new GestorEnlacesDescarga(new RepositorioEnlacesDescargaSql(), new ProveedorDocumentosEnlace()))
        {
        }

        // Para los tests (internal: el contenedor solo ve el constructor público sin parámetros)
        internal DocumentosController(GestorEnlacesDescarga gestor)
        {
            this.gestor = gestor;
        }

        private bool EsEmpleado()
            => (User?.Identity as ClaimsIdentity)?.FindFirst("IsEmployee")?.Value == "true";

        // POST api/Documentos/EnlaceDescarga   { "Tipo": "Pedido", "Empresa": "1", "Numero": "929498" }
        /// <summary>
        /// Da de alta un enlace público (<c>https://api.nuevavision.es/d/{token}</c>, 30 días) y devuelve la URL, la caducidad, el
        /// texto de usted para el cliente y sus móviles en formato internacional. 400 si lo pedido no vale, 403 si no es un empleado
        /// o la serie de la factura no se descarga, 404 si el documento no existe.
        /// </summary>
        [HttpPost]
        [Route("api/Documentos/EnlaceDescarga")]
        [ResponseType(typeof(EnlaceDescargaDTO))]
        public async Task<IHttpActionResult> CrearEnlaceDescarga([FromBody] PeticionEnlaceDescargaDTO peticion)
        {
            if (!EsEmpleado())
            {
                return Content(HttpStatusCode.Forbidden, MOTIVO_SOLO_EMPLEADOS);
            }
            try
            {
                return Ok(await gestor.Crear(peticion ?? new PeticionEnlaceDescargaDTO(), User?.Identity?.Name).ConfigureAwait(false));
            }
            catch (NestoBusinessException ex)
            {
                return ex.StatusCode == HttpStatusCode.BadRequest
                    ? BadRequest(ex.Message)
                    : (IHttpActionResult)Content(ex.StatusCode, ex.Message);
            }
        }
    }

    /// <summary>
    /// NestoAPI#737: <c>GET /d/{token}</c>, anónimo a propósito (como el seguimiento #744 o el pago por enlace): lo único que lo
    /// protege es el token aleatorio de 256 bits. Devuelve el PDF; si el enlace no existe o ha caducado, una página de usted.
    /// </summary>
    [RoutePrefix("d")]
    [AllowAnonymous]
    public class DescargaDocumentoController : ApiController
    {
        private readonly GestorEnlacesDescarga gestorInyectado;

        public DescargaDocumentoController()
        {
        }

        internal DescargaDocumentoController(GestorEnlacesDescarga gestor)
        {
            gestorInyectado = gestor;
        }

        // GET d/Xy3…   (sin usuario)
        [HttpGet]
        [Route("{token}")]
        public async Task<HttpResponseMessage> GetDocumento(string token)
        {
            GestorEnlacesDescarga gestor = gestorInyectado
                ?? new GestorEnlacesDescarga(new RepositorioEnlacesDescargaSql(), new ProveedorDocumentosEnlace());
            DescargaDocumento descarga;
            try
            {
                descarga = await gestor.Descargar(token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[NestoAPI#737] Fallo al abrir el enlace de descarga: {ex.Message}", ex));
                descarga = PaginaEnlacesDescarga.Error();
            }

            HttpResponseMessage respuesta;
            if (descarga.EsPdf)
            {
                respuesta = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(descarga.Pdf) };
                respuesta.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                // inline: el móvil lo abre en el visor; con el nombre, si lo guarda, se llama como el documento
                respuesta.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileName = descarga.NombreFichero };
            }
            else
            {
                respuesta = new HttpResponseMessage(descarga.Estado) { Content = new StringContent(descarga.Html, Encoding.UTF8, "text/html") };
            }
            respuesta.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true, Private = true };
            respuesta.Headers.Add("X-Robots-Tag", "noindex, nofollow");
            respuesta.Headers.Add("Referrer-Policy", "no-referrer");
            return respuesta;
        }
    }
}
