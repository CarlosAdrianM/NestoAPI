using NestoAPI.Infraestructure.ServirJunto;
using NestoAPI.Models.PedidosVenta.ServirJunto;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Endpoint canónico para validar el "Servir junto" de un pedido de venta.
    /// Issue NestoAPI#161: extraído de GanavisionesController porque la propiedad
    /// pertenece al pedido, no al módulo de Ganavisiones. El endpoint antiguo
    /// (api/Ganavisiones/ValidarServirJunto) sigue funcionando para clientes no
    /// actualizados (NestoApp) delegando en este mismo servicio.
    /// </summary>
    [Authorize]
    public class ServirJuntoController : ApiController
    {
        private readonly IServicioValidarServirJunto _servicio;

        public ServirJuntoController(IServicioValidarServirJunto servicio)
        {
            _servicio = servicio;
        }

        [HttpPost]
        [Route("api/PedidosVenta/ValidarServirJunto")]
        [ResponseType(typeof(ValidarServirJuntoResponse))]
        public async Task<IHttpActionResult> ValidarServirJunto([FromBody] ValidarServirJuntoRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Almacen))
            {
                return BadRequest("Debe especificar el almacen del pedido");
            }

            var resultado = await _servicio.Validar(request).ConfigureAwait(false);
            return Ok(resultado);
        }
    }
}
