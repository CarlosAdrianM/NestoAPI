using NestoAPI.Infraestructure.AlbaranesVenta;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    public class AlbaranesVentaController : ApiController
    {
        private readonly IGestorAlbaranesVenta _gestor;
        public readonly IServicioAlbaranesVenta _servicio;

        public AlbaranesVentaController(IGestorAlbaranesVenta gestor, IServicioAlbaranesVenta servicio)
        {
            _gestor = gestor;
            _servicio = servicio;
        }

        [HttpPost]
        [Route("api/AlbaranesVenta/CrearAlbaran")]
        public async Task<IHttpActionResult> CrearAlbaran([FromBody] dynamic parametros)
        {
            string empresa = parametros.Empresa;
            int pedido = parametros.Pedido;
            if (empresa == null)
            {
                return BadRequest("No se ha especificado la empresa");
            }
            if (pedido == 0)
            {
                return BadRequest("No se ha especificado el pedido");
            }
            try
            {
                int albaran = await _gestor.CrearAlbaran(empresa, pedido);
                return Ok(albaran);
            }
            catch (System.Exception ex)
            {
                return InternalServerError(ex);
            }
        }
    }
}
