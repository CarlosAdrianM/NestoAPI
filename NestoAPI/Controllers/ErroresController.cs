using NestoAPI.Infraestructure;
using NestoAPI.Models;
using System.Security.Principal;
using System.Web;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Recibe errores no controlados de las aplicaciones cliente (Nesto, NestoApp, TiendasNuevaVision)
    /// y los registra de forma centralizada en ELMAH. Así los crashes del escritorio dejan rastro,
    /// que de otra forma se perdería (Nesto no tiene ELMAH).
    /// </summary>
    [RoutePrefix("api/Errores")]
    public class ErroresController : ApiController
    {
        /// <summary>NestoAPI#230: uno para toda la API (el controlador se crea en cada petición).</summary>
        private static readonly DeduplicadorErroresCliente DeduplicadorCompartido = new DeduplicadorErroresCliente();

        private readonly ILogService _logService;
        private readonly DeduplicadorErroresCliente _deduplicador;

        public ErroresController(ILogService logService) : this(logService, DeduplicadorCompartido)
        {
        }

        /// <summary>Para los tests: con su propio deduplicador (y su reloj).</summary>
        internal ErroresController(ILogService logService, DeduplicadorErroresCliente deduplicador)
        {
            _logService = logService;
            _deduplicador = deduplicador ?? DeduplicadorCompartido;
        }

        [HttpPost]
        [Route("")]
        // AllowAnonymous a propósito: queremos registrar los crashes de cualquier cliente
        // (Nesto, NestoApp, TiendasNuevaVision) incluso cuando NO hay token válido
        // (pre-login, token caducado, o un fallo en la propia autenticación), que es justo
        // cuando más interesa enterarse. Si la petición SÍ está autenticada, el usuario real
        // sale igualmente del Identity (UserSyncHandler) y se ve en ELMAH.
        [AllowAnonymous]
        public IHttpActionResult Post([FromBody] ErrorClienteDTO error)
        {
            if (error == null || string.IsNullOrWhiteSpace(error.Mensaje))
            {
                return BadRequest("El mensaje del error es obligatorio");
            }

            // NestoAPI#230: este endpoint es [AllowAnonymous], así que UserSyncHandler no propaga
            // de forma fiable el usuario del JWT y ELMAH lo registraría anónimo. Como el cliente
            // (NestoApp/TiendasNuevaVision) manda su identidad en el cuerpo, la ponemos en el
            // contexto para que ELMAH muestre quién sufrió el crash, sin dejar de ser anónimo.
            AsignarUsuarioElmahSiAnonimo(error);

            // NestoAPI#230 (06/11/26): misma app, usuario y mensaje en 24 h → se cuenta y no se loguea
            string usuario = User?.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(User.Identity.Name)
                ? User.Identity.Name
                : error.UsuarioCliente;
            int? repetidosAntes = _deduplicador.Registrar(error.Aplicacion, usuario, error.Mensaje, error.StackTrace);
            if (repetidosAntes == null)
            {
                return Ok();
            }

            string resumen = ConstruirResumen(error) + DeduplicadorErroresCliente.SufijoRepetidos(repetidosAntes.Value);

            var excepcionCliente = new ErrorClienteException(error.Mensaje.Trim(), error.StackTrace);
            _logService.LogError(resumen, excepcionCliente);

            return Ok();
        }

        private static void AsignarUsuarioElmahSiAnonimo(ErrorClienteDTO error)
        {
            HttpContext contexto = HttpContext.Current;
            if (contexto == null)
            {
                return;
            }
            string usuario = UsuarioParaElmah(contexto.User, error);
            if (usuario != null)
            {
                contexto.User = new GenericPrincipal(new GenericIdentity(usuario), new string[0]);
            }
        }

        /// <summary>
        /// Usuario que debe figurar en ELMAH, o null si no hay que tocar el actual. Si ya hay un
        /// usuario autenticado (UserSyncHandler lo propagó del JWT/Windows), se respeta. Si la
        /// petición es anónima, se usa el <see cref="ErrorClienteDTO.UsuarioCliente"/> del cuerpo.
        /// </summary>
        internal static string UsuarioParaElmah(IPrincipal usuarioActual, ErrorClienteDTO error)
        {
            // Si ya hay un usuario autenticado (UserSyncHandler lo propagó del JWT/Windows), respetarlo.
            if (usuarioActual?.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(usuarioActual.Identity.Name))
            {
                return null;
            }
            // Endpoint anónimo: el cliente aporta su identidad en el cuerpo; la usamos para ELMAH.
            return string.IsNullOrWhiteSpace(error?.UsuarioCliente) ? null : error.UsuarioCliente.Trim();
        }

        internal static string ConstruirResumen(ErrorClienteDTO error)
        {
            string app = string.IsNullOrWhiteSpace(error.Aplicacion) ? "Cliente" : error.Aplicacion.Trim();

            string interior = app;
            if (!string.IsNullOrWhiteSpace(error.Version))
            {
                interior += $" {error.Version.Trim()}";
            }
            if (!string.IsNullOrWhiteSpace(error.Plataforma))
            {
                interior += $" ({error.Plataforma.Trim()})";
            }
            string cabecera = $"[{interior}]";

            string contexto = !string.IsNullOrWhiteSpace(error.Contexto)
                ? $" {error.Contexto.Trim()}"
                : string.Empty;

            string tipo = !string.IsNullOrWhiteSpace(error.TipoExcepcion)
                ? $" {error.TipoExcepcion.Trim()}:"
                : string.Empty;

            string usuario = !string.IsNullOrWhiteSpace(error.UsuarioCliente)
                ? $" [usuario: {error.UsuarioCliente.Trim()}]"
                : string.Empty;

            return $"{cabecera}{contexto}{tipo} {error.Mensaje.Trim()}{usuario}";
        }
    }
}
