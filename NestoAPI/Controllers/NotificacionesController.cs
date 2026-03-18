using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using static NestoAPI.Models.Constantes;
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

        [HttpPost]
        [Route("Enviar")]
        [Authorize]
        public async Task<IHttpActionResult> Enviar([FromBody] EnviarNotificacionDTO dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Destinatario))
            {
                return BadRequest("El destinatario es obligatorio");
            }

            if (dto.Notificacion == null || string.IsNullOrWhiteSpace(dto.Notificacion.Titulo))
            {
                return BadRequest("El título de la notificación es obligatorio");
            }

            int enviados = 0;

            switch (dto.TipoDestinatario?.ToLower())
            {
                case "vendedor":
                    enviados = await _servicio.EnviarAVendedor(
                        dto.Empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO,
                        dto.Destinatario,
                        dto.Notificacion
                    ).ConfigureAwait(false);
                    break;
                case "cliente":
                    enviados = await _servicio.EnviarACliente(
                        dto.Empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO,
                        dto.Destinatario,
                        dto.Notificacion
                    ).ConfigureAwait(false);
                    break;
                default:
                    enviados = await _servicio.EnviarAUsuario(
                        dto.Destinatario,
                        dto.Aplicacion ?? "NestoApp",
                        dto.Notificacion
                    ).ConfigureAwait(false);
                    break;
            }

            return Ok(enviados);
        }
    }
}
