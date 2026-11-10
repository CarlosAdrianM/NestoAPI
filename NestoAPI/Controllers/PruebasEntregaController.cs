using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Infraestructure.Agencias.PruebasEntrega;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Prueba de entrega por agencia (10/11/26): un solo endpoint para la prueba de entrega de cualquier agencia. Resuelve el
    /// proveedor por la agencia del envío (<see cref="ServicioPruebasEntrega"/>) y lo pide a la agencia en el momento (no se
    /// guarda copia). Mismos permisos que GET api/RepartoPropio/Envios/{id}/PruebaEntrega, que sigue igual para quien lo use.
    /// </summary>
    [Authorize]
    public class PruebasEntregaController : ApiController
    {
        internal const string MENSAJE_SIN_PERMISO = "Su usuario no tiene permiso para ver la prueba de entrega (son datos personales).";

        private readonly NVEntities db;
        private readonly ServicioPruebasEntrega servicio;

        public PruebasEntregaController() : this(new NVEntities())
        {
        }

        internal PruebasEntregaController(NVEntities db, ServicioPruebasEntrega servicio = null)
        {
            this.db = db;
            this.servicio = servicio ?? ServicioPruebasEntrega.DeProduccion(db);
        }

        // GET api/EnviosAgencias/251500/PruebaEntrega
        /// <summary>
        /// La prueba de entrega del envío, sea de la agencia que sea: 200 con <see cref="PruebaEntregaEnvioDTO"/> (Disponible false y
        /// el motivo en Mensaje si no la hay: agencia sin prueba, aún sin entregar o la agencia no responde); 403 sin permiso; 404 si
        /// el envío no existe. Nunca se guarda en caché (lleva la firma y el DNI).
        /// </summary>
        [HttpGet]
        [Route("api/EnviosAgencias/{id:int}/PruebaEntrega")]
        [ResponseType(typeof(PruebaEntregaEnvioDTO))]
        public async Task<IHttpActionResult> GetPruebaEntrega(int id)
        {
            if (!PermisosRepartoPropio.PuedeVerDatosPersonales(User))
            {
                return Content(HttpStatusCode.Forbidden, new { Message = MENSAJE_SIN_PERMISO });
            }
            // Where + ToListAsync (no FindAsync): ver el «Limit1» de TramitacionEnviosService.
            EnviosAgencia envio = (await db.EnviosAgencias.AsNoTracking().Where(e => e.Numero == id).ToListAsync()).FirstOrDefault();
            if (envio == null)
            {
                return Content(HttpStatusCode.NotFound, new { Message = $"No existe el envío {id}." });
            }
            string nombreAgencia = (await db.AgenciasTransportes.Where(a => a.Numero == envio.Agencia).Select(a => a.Nombre).ToListAsync()).FirstOrDefault();
            PruebaEntregaEnvioDTO prueba = await servicio.Obtener(envio, nombreAgencia);

            HttpResponseMessage respuesta = Request.CreateResponse(HttpStatusCode.OK, prueba);
            // Dato personal: que no se quede en ninguna caché.
            respuesta.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true, Private = true };
            return ResponseMessage(respuesta);
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
