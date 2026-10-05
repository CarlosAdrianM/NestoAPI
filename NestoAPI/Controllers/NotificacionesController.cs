using NestoAPI.Infrastructure;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using static NestoAPI.Models.Constantes;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    [RoutePrefix("api/Notificaciones")]
    public class NotificacionesController : ApiController
    {
        private readonly IServicioNotificacionesPush _servicio;

        /// <summary>Quién tiene Nesto abierto (sustituible en tests).</summary>
        internal Func<List<string>> UsuariosConNestoAbierto { get; set; } = UsuariosConectadosNesto.Usuarios;

        internal const string TIPO_NUEVA_VERSION_NESTO = "NuevaVersionNesto";

        public NotificacionesController(IServicioNotificacionesPush servicio)
        {
            _servicio = servicio;
        }

        /// <summary>
        /// Carlos (25/09/26), ritual del deploy de Nesto: tras publicar la ClickOnce y comprobar Carlos que actualiza
        /// bien, un aviso en la campana a quien tiene Nesto abierto para que salga y vuelva a entrar cuando le venga
        /// bien. Solo Dirección e Informática. Devuelve a cuántos y a quién se ha avisado.
        /// POST api/Notificaciones/NuevaVersionNesto  { "Version": "1.10.32.0" }
        /// </summary>
        [HttpPost]
        [Route("NuevaVersionNesto")]
        [Authorize]
        public async Task<IHttpActionResult> NuevaVersionNesto([FromBody] NuevaVersionNestoDTO dto)
        {
            if (User == null || !(User.IsInRoleSinDominio(GruposSeguridad.DIRECCION) || User.IsInRoleSinDominio(NovedadesController.GRUPO_INFORMATICA)))
            {
                return StatusCode(System.Net.HttpStatusCode.Forbidden);
            }
            string version = dto?.Version?.Trim();
            if (string.IsNullOrWhiteSpace(version) || !System.Version.TryParse(version, out _))
            {
                return BadRequest("Falta la versión (por ejemplo, 1.10.32.0)");
            }
            var notificacion = new NotificacionPushDTO
            {
                Titulo = $"Nesto {version} ya está publicado",
                Cuerpo = string.IsNullOrWhiteSpace(dto.Texto)
                    ? $"Cuando os venga bien, cerrad Nesto y volved a abrirlo para actualizar a la versión {version}. En Novedades tenéis lo que trae."
                    : dto.Texto.Trim(),
                Tipo = TIPO_NUEVA_VERSION_NESTO,
                Datos = new Dictionary<string, string> { ["tipo"] = TIPO_NUEVA_VERSION_NESTO, ["version"] = version }
            };
            List<string> conectados = UsuariosConNestoAbierto() ?? new List<string>();
            // NestoAPI#568: RDS2016 tiene dos núcleos y trece actualizaciones a la vez lo saturan. El
            // script pide primero la lista (SoloListar) y luego avisa por tandas mandando Usuarios.
            if (dto.SoloListar)
            {
                return Ok(new { Version = version, Avisados = 0, Usuarios = conectados });
            }
            List<string> usuarios = UsuariosDeLaTanda(conectados, dto.Usuarios);
            foreach (string usuario in usuarios)
            {
                // El buzón ya avisa por SignalR al guardar: la campana se enciende al momento
                await _servicio.GuardarEnBuzonDeUsuario(usuario, Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
            }
            return Ok(new { Version = version, Avisados = usuarios.Count, Usuarios = usuarios });
        }

        internal const string TIPO_NUEVA_VERSION_NESTO_APP = "NuevaVersionNestoApp";

        /// <summary>
        /// NestoAPI#579, gemelo de <see cref="NuevaVersionNesto"/> para NestoApp: tras promocionar una versión a
        /// Production en AppFlow, una push a todos los móviles con NestoApp diciendo cómo estrenarla. Sin tandas
        /// (cada móvil se actualiza solo). Con <c>Usuarios</c>, solo a esos (para probar primero); con
        /// <c>SoloListar</c>, no manda nada y dice a quién mandaría. Solo Dirección e Informática.
        /// POST api/Notificaciones/NuevaVersionNestoApp  { "Version": "2.22.0", "Texto": null, "SoloListar": false, "Usuarios": null }
        /// </summary>
        [HttpPost]
        [Route("NuevaVersionNestoApp")]
        [Authorize]
        public async Task<IHttpActionResult> NuevaVersionNestoApp([FromBody] NuevaVersionNestoDTO dto)
        {
            if (User == null || !(User.IsInRoleSinDominio(GruposSeguridad.DIRECCION) || User.IsInRoleSinDominio(NovedadesController.GRUPO_INFORMATICA)))
            {
                return StatusCode(System.Net.HttpStatusCode.Forbidden);
            }
            string version = dto?.Version?.Trim();
            if (string.IsNullOrWhiteSpace(version) || !System.Version.TryParse(version, out _))
            {
                return BadRequest("Falta la versión (por ejemplo, 2.22.0)");
            }
            // Live Updates está en modo background: descarga al arrancar en frío y la estrena en el SIGUIENTE
            // arranque en frío. De ahí los dos cierres (probado el 30/09/26 con la 2.22.0).
            var notificacion = new NotificacionPushDTO
            {
                Titulo = $"NestoApp {version} ya está disponible",
                Cuerpo = string.IsNullOrWhiteSpace(dto.Texto)
                    ? "Para estrenarla: cierra la app del todo (quítala de las apps recientes), ábrela con conexión y espera " +
                      "unos segundos. Luego ciérrala del todo otra vez y vuelve a abrirla. " +
                      $"En tu perfil verás «Versión actualización {version}»."
                    : dto.Texto.Trim(),
                Tipo = TIPO_NUEVA_VERSION_NESTO_APP,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NUEVA_VERSION_NESTO_APP,
                    ["version"] = version,
                    ["ruta"] = "/profile"
                }
            };

            List<string> activos = await _servicio.UsuariosConDispositivoActivo(Aplicaciones.NESTO_APP).ConfigureAwait(false)
                ?? new List<string>();
            if (dto.SoloListar)
            {
                return Ok(new { Version = version, Avisados = 0, Usuarios = activos });
            }

            List<string> pedidos = (dto.Usuarios ?? new List<string>())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (pedidos.Count == 0)
            {
                int dispositivos = await _servicio.EnviarATodosDeAplicacion(Aplicaciones.NESTO_APP, notificacion).ConfigureAwait(false);
                return Ok(new { Version = version, Avisados = activos.Count, Usuarios = activos, Dispositivos = dispositivos });
            }

            var avisados = new List<string>();
            foreach (string usuario in pedidos)
            {
                if (await _servicio.EnviarAUsuario(usuario, Aplicaciones.NESTO_APP, notificacion).ConfigureAwait(false) > 0)
                {
                    avisados.Add(usuario);
                }
            }
            return Ok(new { Version = version, Avisados = avisados.Count, Usuarios = avisados });
        }

        /// <summary>
        /// A quién se avisa en esta llamada: a todos los que tienen Nesto abierto, o solo a los de la
        /// tanda pedida que lo sigan teniendo abierto (con o sin el dominio delante, sin mirar mayúsculas).
        /// </summary>
        internal static List<string> UsuariosDeLaTanda(List<string> conectados, List<string> pedidos)
        {
            if (pedidos == null || !pedidos.Any(p => !string.IsNullOrWhiteSpace(p)))
            {
                return conectados;
            }
            string SinDominio(string usuario) => usuario.Trim().Substring(usuario.Trim().LastIndexOf('\\') + 1);
            var tanda = new HashSet<string>(
                pedidos.Where(p => !string.IsNullOrWhiteSpace(p)).Select(SinDominio), StringComparer.OrdinalIgnoreCase);
            return conectados.Where(c => !string.IsNullOrWhiteSpace(c) && tanda.Contains(SinDominio(c))).ToList();
        }

        internal const string TIPO_AVISO_NESTO = "AvisoNesto";

        /// <summary>
        /// 28/09/26 (Carlos): un aviso en la campana de Nesto a usuarios concretos (p. ej. a Alfredo al encender la
        /// nota de entrega automática), sin correo. El buzón avisa por SignalR al guardar. Solo Dirección e Informática.
        /// POST api/Notificaciones/AvisoNesto  { "Usuarios": ["Alfredo"], "Titulo": "...", "Texto": "..." }
        /// </summary>
        [HttpPost]
        [Route("AvisoNesto")]
        [Authorize]
        public async Task<IHttpActionResult> AvisoNesto([FromBody] AvisoNestoDTO dto)
        {
            if (User == null || !(User.IsInRoleSinDominio(GruposSeguridad.DIRECCION) || User.IsInRoleSinDominio(NovedadesController.GRUPO_INFORMATICA)))
            {
                return StatusCode(System.Net.HttpStatusCode.Forbidden);
            }
            List<string> usuarios = (dto?.Usuarios ?? new List<string>())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Trim().Contains("\\") ? u.Trim() : "NUEVAVISION\\" + u.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (usuarios.Count == 0 || string.IsNullOrWhiteSpace(dto.Titulo) || string.IsNullOrWhiteSpace(dto.Texto))
            {
                return BadRequest("Faltan los usuarios, el título o el texto del aviso");
            }
            var notificacion = new NotificacionPushDTO
            {
                Titulo = dto.Titulo.Trim(),
                Cuerpo = dto.Texto.Trim(),
                Tipo = TIPO_AVISO_NESTO,
                Datos = new Dictionary<string, string> { ["tipo"] = TIPO_AVISO_NESTO }
            };
            foreach (string usuario in usuarios)
            {
                await _servicio.GuardarEnBuzonDeUsuario(usuario, Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
            }
            return Ok(new { Avisados = usuarios.Count, Usuarios = usuarios });
        }

        internal const string TIPO_AVISO_ARIADNA = "AvisoAriadna";

        /// <summary>
        /// NestoAPI#575 (fase 1): un aviso en el buzón de Ariadna, la app de almacén, a usuarios concretos.
        /// Ruta nueva y aparte: no toca los avisos de Nesto ni de NestoApp. Los usuarios de Ariadna entran
        /// como los de NestoApp, así que van sin dominio («Santiago»); si llega con dominio, se le quita.
        /// La app lee su buzón con las rutas de siempre: api/Notificaciones/Buzon?aplicacion=Ariadna.
        /// Solo Dirección e Informática.
        /// POST api/Notificaciones/AvisoAriadna  { "Usuarios": ["Santiago"], "Titulo": "...", "Texto": "..." }
        /// </summary>
        [HttpPost]
        [Route("AvisoAriadna")]
        [Authorize]
        public async Task<IHttpActionResult> AvisoAriadna([FromBody] AvisoNestoDTO dto)
        {
            if (User == null || !(User.IsInRoleSinDominio(GruposSeguridad.DIRECCION) || User.IsInRoleSinDominio(NovedadesController.GRUPO_INFORMATICA)))
            {
                return StatusCode(System.Net.HttpStatusCode.Forbidden);
            }
            List<string> usuarios = (dto?.Usuarios ?? new List<string>())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Trim().Substring(u.Trim().LastIndexOf('\\') + 1).Trim())
                .Where(u => u.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (usuarios.Count == 0 || string.IsNullOrWhiteSpace(dto.Titulo) || string.IsNullOrWhiteSpace(dto.Texto))
            {
                return BadRequest("Faltan los usuarios, el título o el texto del aviso");
            }
            var notificacion = new NotificacionPushDTO
            {
                Titulo = dto.Titulo.Trim(),
                Cuerpo = dto.Texto.Trim(),
                Tipo = TIPO_AVISO_ARIADNA,
                Datos = new Dictionary<string, string> { ["tipo"] = TIPO_AVISO_ARIADNA }
            };
            foreach (string usuario in usuarios)
            {
                await _servicio.GuardarEnBuzonDeUsuario(usuario, Aplicaciones.ARIADNA, notificacion).ConfigureAwait(false);
            }
            return Ok(new { Avisados = usuarios.Count, Usuarios = usuarios });
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

        /// <summary>
        /// NestoAPI#575: el usuario que llama deja de recibir avisos en este aparato (un mozo que se quita de una PDA de
        /// Ariadna). Los demás que comparten el token siguen recibiendo.
        /// </summary>
        [HttpDelete]
        [Route("Dispositivos/Mio")]
        [Authorize]
        public async Task<IHttpActionResult> DesregistrarMiDispositivo([FromBody] string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return BadRequest("El token es obligatorio");
            }

            string usuario = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return Unauthorized();
            }

            bool desregistrado = await _servicio.DesregistrarDispositivoDeUsuario(token, usuario).ConfigureAwait(false);
            return desregistrado ? (IHttpActionResult)Ok() : NotFound();
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

        /// <summary>
        /// Buzón persistente (#387): las notificaciones que se enviaron a este usuario, para que la
        /// app pueda volver a verlas aunque se descartara la notificación del sistema.
        /// </summary>
        [HttpGet]
        [Route("Buzon")]
        [Authorize]
        public async Task<IHttpActionResult> ObtenerBuzon(string aplicacion = null, bool soloNoLeidas = false, int pagina = 1, int tamanoPagina = 20)
        {
            string usuario = User?.Identity?.Name;

            if (string.IsNullOrWhiteSpace(usuario))
            {
                return Unauthorized();
            }

            List<NotificacionBuzonDTO> notificaciones = await _servicio.ObtenerBuzon(
                usuario,
                aplicacion ?? Constantes.Aplicaciones.NESTO_APP,
                soloNoLeidas,
                pagina,
                tamanoPagina
            ).ConfigureAwait(false);

            return Ok(notificaciones);
        }

        /// <summary>Contador para el badge del menú.</summary>
        [HttpGet]
        [Route("Buzon/NoLeidas")]
        [Authorize]
        public async Task<IHttpActionResult> ContarNoLeidas(string aplicacion = null)
        {
            string usuario = User?.Identity?.Name;

            if (string.IsNullOrWhiteSpace(usuario))
            {
                return Unauthorized();
            }

            int noLeidas = await _servicio.ContarNoLeidas(
                usuario,
                aplicacion ?? Constantes.Aplicaciones.NESTO_APP
            ).ConfigureAwait(false);

            return Ok(noLeidas);
        }

        [HttpPut]
        [Route("Buzon/{id:int}/Leida")]
        [Authorize]
        public async Task<IHttpActionResult> MarcarLeida(int id)
        {
            string usuario = User?.Identity?.Name;

            if (string.IsNullOrWhiteSpace(usuario))
            {
                return Unauthorized();
            }

            bool marcada = await _servicio.MarcarLeida(id, usuario).ConfigureAwait(false);

            // Si la notificación no es de este usuario se responde NotFound, no Forbidden: así no se
            // confirma desde fuera que ese id existe.
            return marcada ? (IHttpActionResult)Ok() : NotFound();
        }

        [HttpPut]
        [Route("Buzon/Leidas")]
        [Authorize]
        public async Task<IHttpActionResult> MarcarTodasLeidas(string aplicacion = null)
        {
            string usuario = User?.Identity?.Name;

            if (string.IsNullOrWhiteSpace(usuario))
            {
                return Unauthorized();
            }

            int marcadas = await _servicio.MarcarTodasLeidas(
                usuario,
                aplicacion ?? Constantes.Aplicaciones.NESTO_APP
            ).ConfigureAwait(false);

            return Ok(marcadas);
        }

        [HttpDelete]
        [Route("Buzon/{id:int}")]
        [Authorize]
        public async Task<IHttpActionResult> EliminarDelBuzon(int id)
        {
            string usuario = User?.Identity?.Name;

            if (string.IsNullOrWhiteSpace(usuario))
            {
                return Unauthorized();
            }

            bool eliminada = await _servicio.EliminarDelBuzon(id, usuario).ConfigureAwait(false);

            return eliminada ? (IHttpActionResult)Ok() : NotFound();
        }

        [HttpPost]
        [Route("NuevoProtocolo")]
        public async Task<IHttpActionResult> NotificarNuevoProtocolo([FromBody] NuevoProtocoloDTO dto)
        {
            string apiKeyEsperada = ConfigurationManager.AppSettings["NotificacionesApiKey"];
            string apiKeyRecibida = Request?.Headers?.Authorization?.Parameter
                ?? Request?.Headers?.Authorization?.Scheme;

            // NestoAPI#429: esta comprobación ya fallaba en cerrado (a diferencia de la de
            // PrestashopLogin), pero comparaba con string.Equals, que no es de tiempo constante.
            // Ahora las dos copias usan el mismo núcleo. La cabecera se sigue leyendo aquí y no
            // con [ApiKey] porque NVIA manda la clave en Authorization de forma peculiar —unas
            // veces como Parameter y otras como Scheme—, y cambiar eso sería romperle el envío de
            // protocolos nuevos sin ganar nada.
            if (!Infraestructure.Seguridad.ValidadorApiKey.EsValida(apiKeyEsperada, apiKeyRecibida))
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
