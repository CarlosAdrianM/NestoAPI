using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Campanas.OfertasParaTi;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.SugerenciasProductos;
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

        public CampanasPushController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            var nucleo = new ServicioOfertasParaTi(new LectorOfertasParaTiSql(db),
                new PuntuadorReposicionModelo(new RepositorioSugerenciasProductosSql(db), PredictorSugerenciasProductos.Instancia));
            servicio = new ServicioPushCampana(nucleo, new FuentePushCampanaSql(db), new ServicioNotificacionesPush());
        }

        internal CampanasPushController(ServicioPushCampana servicio)
        {
            this.servicio = servicio;
        }

        /// <summary>Ahora (sustituible en las pruebas).</summary>
        internal Func<DateTime> Ahora { get; set; } = () => DateTime.Now;

        // POST api/Campanas/Push?ensayo=true
        /// <summary>
        /// A cada cliente con la app (consejos encendidos y sin baja en «Avisos de reposición»), sus ofertas de la campaña y su push. 400
        /// con la petición mal o, de verdad, fuera de 9:00 a 21:00; 403 sin permiso.
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
