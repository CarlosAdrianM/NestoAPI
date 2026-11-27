using Hangfire;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Campanas.OfertasParaTi;
using NestoAPI.Models;
using System;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// «Ofertas para ti»: la push de campaña personalizable a los clientes de la app (<see cref="ServicioPushCampana"/>). Ensayo por
    /// DEFECTO (no envía ni escribe nada). Solo Dirección e Informática (403 de usted). Aparte de CampanasController, que es el
    /// mantenimiento de las filas.
    /// </summary>
    [Authorize]
    public class CampanasPushController : ApiController
    {
        private readonly NVEntities db;
        private readonly ServicioPushCampana servicio;
        private readonly Func<PeticionCampanaPushDTO, string, string> encolar;

        public CampanasPushController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            servicio = ServicioPushCampana.Produccion(db);
            encolar = (peticion, usuario) => BackgroundJob.Enqueue(() => CampanaPushJobs.Enviar(peticion, usuario));
        }

        /// <param name="encolar">(petición, usuario) → id del trabajo de Hangfire del envío de verdad.</param>
        internal CampanasPushController(ServicioPushCampana servicio, Func<PeticionCampanaPushDTO, string, string> encolar = null)
        {
            this.servicio = servicio;
            this.encolar = encolar ?? ((p, u) => "0");
        }

        /// <summary>Ahora (sustituible en las pruebas).</summary>
        internal Func<DateTime> Ahora { get; set; } = () => DateTime.Now;

        // POST api/Campanas/Push?ensayo=true
        /// <summary>
        /// A cada cliente con la app (consejos encendidos y sin baja en «Avisos de reposición»), sus ofertas de la campaña y su push. En
        /// ensayo (por defecto), 200 con todo lo que se mandaría (síncrono, solo lee). De verdad, 202 con el id del trabajo: el envío va en
        /// segundo plano y el resultado queda en Modificaciones (Tabla CampanaPush). 400 con la petición mal, con el momento «Cierre» si la
        /// campaña no acaba hoy o mañana o, de verdad, fuera de 9:00 a 21:00; 403 sin permiso. <c>Momento</c>: Arranque (por defecto),
        /// Recordatorio o Cierre (<see cref="ReglaMomentoPushCampana"/>).
        /// </summary>
        [HttpPost]
        [Route("api/Campanas/Push")]
        [ResponseType(typeof(RespuestaCampanaPushDTO))]
        public async Task<IHttpActionResult> PostPush([FromBody] PeticionCampanaPushDTO peticion, bool ensayo = true)
        {
            if (!ReglaPushCampana.PuedeLanzar(User))
            {
                return Content(HttpStatusCode.Forbidden, ReglaPushCampana.MENSAJE_SIN_PERMISO);
            }
            if (!ensayo)
            {
                // La petición, el horario y, en el cierre, que la campaña acabe hoy o mañana: el 400 en el acto, antes de encolar
                string error = await servicio.ValidarAntesDeEncolar(peticion, Ahora()).ConfigureAwait(false);
                if (error != null)
                {
                    return BadRequest(error);
                }
                string id = encolar(peticion, UsuarioAuditoriaHelper.Resolver(User, null));
                return Content(HttpStatusCode.Accepted, new PushCampanaEncoladaDTO
                {
                    JobId = id,
                    Campana = peticion.Campana.Trim(),
                    Mensaje = "La push se está enviando en segundo plano. El resultado quedará en Modificaciones (Tabla CampanaPush) en unos minutos."
                });
            }
            ResultadoPushCampana resultado = await servicio.Ejecutar(peticion, ensayo, Ahora(), UsuarioAuditoriaHelper.Resolver(User, null))
                .ConfigureAwait(false);
            return resultado.Estado == EstadoPushCampana.PeticionIncorrecta
                ? BadRequest(resultado.Mensaje)
                : (IHttpActionResult)Ok(resultado.Respuesta);
        }

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
