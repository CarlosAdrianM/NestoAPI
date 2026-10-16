using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Pagos.Aplazame;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#663: la notificación de Aplázame (<c>merchant.notification_url</c> del checkout). Aquí nos pide confirmar el
    /// cobro cuando ha aprobado la financiación (<c>pending</c> + <c>confirmation_required</c>) y nos avisa de cómo acaba
    /// (<c>ok</c> / <c>ko</c>).
    ///
    /// <para><b>Sin JWT</b> ([AllowAnonymous]): quien llama es Aplázame, que se identifica con <c>Authorization: Bearer
    /// &lt;nuestra clave privada&gt;</c>. Esa cabecera NO es un JWT, y el middleware JWT de OWIN (Startup,
    /// UseJwtBearerAuthentication en modo activo) lo único que hace con un bearer que no sabe validar es dejar la petición
    /// sin usuario: ni la corta ni quita la cabecera (lo prueba AplazameControllerTests.MiddlewareJwt_...). Por eso la
    /// clave se comprueba aquí, en tiempo constante, y sin ella se contesta 403.</para>
    /// </summary>
    [RoutePrefix("api/Aplazame")]
    public class AplazameController : ApiController
    {
        private readonly IServicioCobrosAplazame servicio;

        public AplazameController() : this(ServicioCobrosAplazame.CrearPorDefecto())
        {
        }

        public AplazameController(IServicioCobrosAplazame servicio)
        {
            this.servicio = servicio;
        }

        // POST: api/Aplazame/Notificacion
        [HttpPost]
        [Route("Notificacion")]
        [AllowAnonymous]
        public async Task<HttpResponseMessage> Notificacion()
        {
            string autorizacion = CabeceraAutorizacion(Request);
            string cuerpo = Request.Content == null ? null : await Request.Content.ReadAsStringAsync().ConfigureAwait(false);

            RespuestaNotificacionAplazame respuesta;
            try
            {
                respuesta = await servicio.ProcesarNotificacion(autorizacion, cuerpo).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Un 500 hace que Aplázame reintente, que es lo que queremos si algo ha fallado de verdad aquí
                ElmahHelper.Log(new Exception($"[Aplázame #663] Error al procesar la notificación: {ex.Message}", ex));
                return Request.CreateResponse(HttpStatusCode.InternalServerError);
            }

            HttpResponseMessage http = Request.CreateResponse((HttpStatusCode)respuesta.CodigoHttp);
            if (respuesta.Status != null)
            {
                http.Content = new StringContent("{\"status\":\"" + respuesta.Status + "\"}", Encoding.UTF8, "application/json");
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
    }
}
