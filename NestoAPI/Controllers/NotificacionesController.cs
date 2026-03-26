using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
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
        [Route("RegistrarDispositivo")]
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
                aplicacion ?? Constantes.Aplicaciones.NESTO_APP
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
                        dto.Aplicacion ?? Constantes.Aplicaciones.NESTO_APP,
                        dto.Notificacion
                    ).ConfigureAwait(false);
                    break;
            }

            return Ok(enviados);
        }

        [HttpPost]
        [Route("NuevoProtocolo")]
        public async Task<IHttpActionResult> NotificarNuevoProtocolo([FromBody] NuevoProtocoloDTO dto)
        {
            string apiKeyEsperada = ConfigurationManager.AppSettings["NotificacionesApiKey"];
            string apiKeyRecibida = Request?.Headers?.Authorization?.Parameter
                ?? Request?.Headers?.Authorization?.Scheme;

            if (string.IsNullOrWhiteSpace(apiKeyEsperada) ||
                !string.Equals(apiKeyEsperada, apiKeyRecibida, StringComparison.Ordinal))
            {
                return Unauthorized();
            }

            if (dto == null || string.IsNullOrWhiteSpace(dto.Titulo))
            {
                return BadRequest("El titulo del protocolo es obligatorio");
            }

            var notificacion = new NotificacionPushDTO
            {
                Titulo = "Nuevo protocolo disponible",
                Cuerpo = dto.Titulo,
                Datos = new Dictionary<string, string>
                {
                    { "tipo", "protocolo" }
                }
            };

            if (dto.VideoId.HasValue)
            {
                notificacion.Datos["videoId"] = dto.VideoId.Value.ToString();
            }

            if (!string.IsNullOrWhiteSpace(dto.ImagenUrl))
            {
                notificacion.Datos["imagenUrl"] = dto.ImagenUrl;
            }

            int enviados = await _servicio.EnviarATodosDeAplicacion(
                Constantes.Aplicaciones.NESTO_TIENDAS, notificacion).ConfigureAwait(false);

            return Ok(enviados);
        }
    }
}
