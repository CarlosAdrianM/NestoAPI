using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models.Pagos;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    [RoutePrefix("api/Pagos")]
    public class PagosController : ApiController
    {
        private readonly IServicioPagos _servicioPagos;

        public PagosController(IServicioPagos servicioPagos)
        {
            _servicioPagos = servicioPagos;
        }

        [HttpPost]
        [Route("")]
        [Authorize]
        public async Task<IHttpActionResult> IniciarPago([FromBody] SolicitudPagoTPV solicitud)
        {
            if (solicitud == null)
            {
                return BadRequest("La solicitud de pago es obligatoria");
            }

            string usuario = User?.Identity?.Name ?? "Desconocido";
            RespuestaIniciarPago respuesta = await _servicioPagos.IniciarPago(solicitud, usuario).ConfigureAwait(false);
            return Ok(respuesta);
        }

        [HttpPost]
        [Route("NotificacionRedsys")]
        [AllowAnonymous]
        public async Task<IHttpActionResult> NotificacionRedsys([FromBody] NotificacionRedsys notificacion)
        {
            // Siempre devolver 200 para que Redsys no reintente
            if (notificacion != null)
            {
                await _servicioPagos.ProcesarNotificacion(notificacion).ConfigureAwait(false);
            }

            return Ok();
        }

        [HttpGet]
        [Route("{idPago:int}")]
        [Authorize]
        public async Task<IHttpActionResult> ConsultarPago(int idPago)
        {
            PagoTPVDTO pago = await _servicioPagos.ConsultarPago(idPago).ConfigureAwait(false);

            if (pago == null)
            {
                return NotFound();
            }

            return Ok(pago);
        }

        [HttpGet]
        [Route("Auditoria/{numeroOrden}")]
        [Authorize]
        public async Task<IHttpActionResult> ConsultarAuditoria(string numeroOrden)
        {
            PagoTPVDTO pago = await _servicioPagos.ConsultarAuditoria(numeroOrden).ConfigureAwait(false);

            if (pago == null)
            {
                return NotFound();
            }

            return Ok(pago);
        }
    }
}
