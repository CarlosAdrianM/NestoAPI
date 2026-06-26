using System;
using System.Web;
using Elmah;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Logueo a ELMAH garantizando SIEMPRE un usuario. ELMAH toma el usuario de
    /// <c>HttpContext.Current.User</c> (que <see cref="UserSyncHandler"/> rellena en las peticiones HTTP
    /// autenticadas), pero los procesos en segundo plano (jobs de Hangfire) no tienen HttpContext ni
    /// principal, así que el error quedaba sin usuario y no se sabía si lo había disparado una persona
    /// (a quién preguntar) o un proceso automático. Cuando no hay usuario autenticado, se estampa un
    /// <paramref name="usuarioFallback"/> descriptivo (p. ej. "Sistema (seguimiento de envíos)").
    ///
    /// Mismo patrón que <c>ServicioFacturas</c>: cuando hay petición HTTP, ELMAH ya pone el usuario real
    /// y el fallback NO se aplica; solo rellena el hueco de los jobs. Nunca lanza: el logueo no debe
    /// romper el flujo que lo invoca.
    /// </summary>
    public static class ElmahHelper
    {
        public static void Log(Exception excepcion, string usuarioFallback)
        {
            if (excepcion == null)
            {
                return;
            }
            try
            {
                HttpContext httpContext = HttpContext.Current;
                Error error = httpContext != null ? new Error(excepcion, httpContext) : new Error(excepcion);
                if (string.IsNullOrWhiteSpace(error.User) && !string.IsNullOrWhiteSpace(usuarioFallback))
                {
                    error.User = usuarioFallback;
                }
                ErrorLog.GetDefault(httpContext)?.Log(error);
            }
            catch
            {
                // El logueo a ELMAH NUNCA debe romper el flujo que lo invoca.
            }
        }
    }
}
