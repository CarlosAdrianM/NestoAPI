using NestoAPI.Infraestructure;
using NestoAPI.Models;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    [RoutePrefix("api/ReclamacionDeuda")]
    public class ReclamacionDeudaController : ApiController
    {
        private readonly IServicioReclamacionDeuda _servicio;

        public ReclamacionDeudaController(IServicioReclamacionDeuda servicio)
        {
            _servicio = servicio;
        }

        [HttpPost]
        [Route("")]
        [ResponseType(typeof(ReclamacionDeuda))]
        public async Task<IHttpActionResult> Post([FromBody] ReclamacionDeuda reclamacion)
        {
            string usuario = User?.Identity?.Name;
            ReclamacionDeuda respuesta = await _servicio.ProcesarReclamacionDeuda(reclamacion, usuario).ConfigureAwait(false);
            return Ok(respuesta);
        }
    }
}
