using Hangfire;
using System;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos.Aplazame
{
    /// <summary>
    /// NestoAPI#663: los trabajos de Hangfire de Aplázame.
    /// <list type="bullet">
    /// <item><description><b>Captura</b> (recurrente, cada hora en horario laboral): captura los cobros confirmados cuyo pedido
    /// ya está entero en albarán o factura, y avisa a Informática de los que lleven 12 días sin poder capturarse.</description></item>
    /// <item><description><b>Aviso de aprobación</b> (puntual, 2 minutos después de confirmar un cobro): push al cliente si su
    /// cobro está aprobado y sigue sin pedido.</description></item>
    /// </list>
    /// Los cobros confirmados que se quedan sin pedido los cancela el job de cobros huérfanos del carrito
    /// (<see cref="CobrosCarritoJobsService"/>), que es el mismo para Redsys y para Aplázame.
    /// </summary>
    public static class AplazameJobsService
    {
        // Sin reintentos: si falla, dentro de una hora vuelve a pasar. Y nunca dos a la vez, que capturarían dos veces.
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static async Task CapturarCobrosServidos()
        {
            IServicioCobrosAplazame servicio = ServicioCobrosAplazame.CrearPorDefecto();
            if (string.IsNullOrWhiteSpace(servicio.Configuracion.ClavePrivada))
            {
                return; // sin claves no hay cobros de Aplázame que capturar
            }
            _ = await servicio.CapturarServidos(DateTime.Now).ConfigureAwait(false);
        }

        [AutomaticRetry(Attempts = 0)]
        public static Task AvisarAprobacionPendiente(int idPago)
        {
            return ServicioCobrosAplazame.CrearPorDefecto().AvisarAprobacionSiSigueSinPedido(idPago);
        }

        /// <summary>Programa la push de «ya está aprobado» para dentro de 2 minutos.</summary>
        internal static void ProgramarAvisoAprobacion(int idPago)
        {
            _ = BackgroundJob.Schedule(() => AvisarAprobacionPendiente(idPago),
                TimeSpan.FromMinutes(ServicioCobrosAplazame.MINUTOS_AVISO_APROBACION));
        }
    }
}
