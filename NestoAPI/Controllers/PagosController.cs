using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models.Pagos;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
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

            if (solicitud.Efectos != null && solicitud.Efectos.Any())
            {
                decimal sumaEfectos = solicitud.Efectos.Sum(e => e.Importe);
                if (sumaEfectos != solicitud.Importe)
                {
                    return BadRequest($"La suma de los efectos ({sumaEfectos}) no coincide con el importe total ({solicitud.Importe})");
                }
            }

            string usuario = User?.Identity?.Name ?? "Desconocido";
            RespuestaIniciarPago respuesta = await _servicioPagos.IniciarPago(solicitud, usuario).ConfigureAwait(false);
            return Ok(respuesta);
        }

        [HttpPost]
        [Route("NotificacionRedsys")]
        [AllowAnonymous]
        public async Task<IHttpActionResult> NotificacionRedsys()
        {
            // Redsys envia la notificacion como application/x-www-form-urlencoded
            NameValueCollection formData = await Request.Content.ReadAsFormDataAsync().ConfigureAwait(false);

            if (formData == null)
            {
                return Ok();
            }

            var notificacion = new NotificacionRedsys
            {
                Ds_SignatureVersion = formData["Ds_SignatureVersion"],
                Ds_MerchantParameters = formData["Ds_MerchantParameters"],
                Ds_Signature = formData["Ds_Signature"]
            };

            // Siempre devolver 200 para que Redsys no reintente
            await _servicioPagos.ProcesarNotificacion(notificacion).ConfigureAwait(false);

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
