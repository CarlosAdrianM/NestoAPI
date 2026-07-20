using System;
using System.Transactions;
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
        /// <summary>
        /// NestoAPI#182: ejecuta una escritura a ELMAH FUERA de la transacción ambiente.
        /// ELMAH escribe en la MISMA base de datos, así que si el error se loguea desde dentro de
        /// un TransactionScope (p. ej. PutPedidoVenta llamado por UnirPedidos, o la facturación),
        /// su conexión se alista en esa transacción y falla con "The underlying provider failed on
        /// Open" —o intenta promover a transacción distribuida, que MSDTC no permite—, de modo que
        /// el error se PIERDE justo cuando más falta hace. Con Suppress se escribe aunque después
        /// se haga rollback, que es exactamente lo que queremos: el error ocurrió.
        /// Nunca lanza: el logueo no debe romper el flujo que lo invoca.
        /// </summary>
        public static void SinTransaccion(Action escribirEnElmah)
        {
            if (escribirEnElmah == null)
            {
                return;
            }
            try
            {
                using (TransactionScope sinTransaccion = new TransactionScope(TransactionScopeOption.Suppress))
                {
                    escribirEnElmah();
                    sinTransaccion.Complete();
                }
            }
            catch
            {
                // El logueo a ELMAH NUNCA debe romper el flujo que lo invoca.
            }
        }

        /// <summary>Loguea a ELMAH sin usuario de respaldo (peticiones HTTP, donde ya lo pone ELMAH).</summary>
        public static void Log(Exception excepcion)
        {
            Log(excepcion, null);
        }

        public static void Log(Exception excepcion, string usuarioFallback)
        {
            if (excepcion == null)
            {
                return;
            }
            SinTransaccion(() =>
            {
                HttpContext httpContext = HttpContext.Current;
                Error error = httpContext != null ? new Error(excepcion, httpContext) : new Error(excepcion);
                if (string.IsNullOrWhiteSpace(error.User) && !string.IsNullOrWhiteSpace(usuarioFallback))
                {
                    error.User = usuarioFallback;
                }
                ErrorLog.GetDefault(httpContext)?.Log(error);
            });
        }

        /// <summary>
        /// NestoAPI#182: equivalente a <c>ErrorSignal.FromCurrentContext().Raise(ex)</c> pero seguro
        /// dentro de transacciones. Respeta el errorFilter de Web.config (que ErrorLog.Log se salta),
        /// por eso hay dos caminos: con HttpContext se usa la señal, y sin él el log directo.
        /// </summary>
        public static void Señalar(Exception excepcion)
        {
            if (excepcion == null)
            {
                return;
            }
            SinTransaccion(() =>
            {
                HttpContext httpContext = HttpContext.Current;
                if (httpContext != null)
                {
                    ErrorSignal.FromContext(httpContext).Raise(excepcion, httpContext);
                }
                else
                {
                    ErrorLog.GetDefault(null)?.Log(new Error(excepcion));
                }
            });
        }
    }
}
