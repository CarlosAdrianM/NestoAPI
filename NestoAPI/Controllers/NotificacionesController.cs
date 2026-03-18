using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    [RoutePrefix("api/Notificaciones")]
    public class NotificacionesController : ApiController
    {
        private readonly IServicioNotificacionesPush _servicio;

        public NotificacionesController(IServicioNotificacionesPush servicio)
        {
            _servicio = servicio;
        }

        [HttpPost]
        [Route("Dispositivos")]
        [Authorize]
        public async Task<IHttpActionResult> RegistrarDispositivo([FromBody] RegistrarDispositivoDTO registro)
        {
            if (registro == null)
            {
                return BadRequest("Los datos del dispositivo son obligatorios");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            string usuario = User?.Identity?.Name ?? "Desconocido";

            try
            {
                DispositivoNotificacion resultado = await _servicio.RegistrarDispositivo(registro, usuario).ConfigureAwait(false);
                return Ok(resultado);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete]
        [Route("Dispositivos")]
        [Authorize]
        public async Task<IHttpActionResult> DesregistrarDispositivo([FromBody] string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return BadRequest("El token es obligatorio");
            }

            bool desregistrado = await _servicio.DesregistrarDispositivo(token).ConfigureAwait(false);

            if (!desregistrado)
            {
                return NotFound();
            }

            return Ok();
        }

        [HttpGet]
        [Route("Dispositivos")]
        [Authorize]
        public async Task<IHttpActionResult> ObtenerDispositivos(string aplicacion = null)
        {
            string usuario = User?.Identity?.Name;

            if (string.IsNullOrWhiteSpace(usuario))
            {
                return Unauthorized();
            }

            var dispositivos = await _servicio.ObtenerDispositivosUsuario(
                usuario,
                aplicacion ?? "NestoApp"
            ).ConfigureAwait(false);

            return Ok(dispositivos);
        }
    }
}
