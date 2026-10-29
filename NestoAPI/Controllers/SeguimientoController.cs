using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#744: la página pública de seguimiento de los envíos del reparto propio (agencia 14).
    /// URL: https://api.nuevavision.es/seguimiento/{token} (el token lo da <see cref="Models.Agencias.EnlaceSeguimientoRepartoPropio"/>).
    /// Anónima a propósito, como la de pago por enlace: lo único que la protege es la firma del token. Un token malo, un envío
    /// que no existe o uno que no es del reparto propio dan el mismo 404, con una página amable.
    /// </summary>
    [RoutePrefix("seguimiento")]
    [AllowAnonymous]
    public class SeguimientoController : ApiController
    {
        private readonly ServicioSeguimientoPublicoReparto servicioInyectado;

        public SeguimientoController()
        {
        }

        internal SeguimientoController(ServicioSeguimientoPublicoReparto servicio)
        {
            servicioInyectado = servicio;
        }

        // GET seguimiento/AAAAe0x…   (sin usuario)
        [HttpGet]
        [Route("{token}")]
        public async Task<HttpResponseMessage> GetSeguimiento(string token)
        {
            PaginaSeguimientoReparto pagina;
            if (servicioInyectado != null)
            {
                pagina = await servicioInyectado.Pagina(token).ConfigureAwait(false);
            }
            else
            {
                using (var db = new NVEntities())
                {
                    pagina = await new ServicioSeguimientoPublicoReparto(new LectorSeguimientoPublicoReparto(db)).Pagina(token).ConfigureAwait(false);
                }
            }
            var respuesta = new HttpResponseMessage(pagina.Estado)
            {
                Content = new StringContent(pagina.Html, Encoding.UTF8, "text/html")
            };
            // Que no se guarde en cachés compartidas ni lo indexen los buscadores, ni se filtre el enlace al seguir otro.
            respuesta.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true, Private = true };
            respuesta.Headers.Add("X-Robots-Tag", "noindex, nofollow");
            respuesta.Headers.Add("Referrer-Policy", "no-referrer");
            return respuesta;
        }
    }
}
