using System;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// NestoAPI#820 (incidente 04-05/11/26): un servicio de fuera (Pub/Sub, una tienda, una agencia…) no ha contestado en el
    /// tiempo que se le da. Es un tipo propio para que quien recorre una cola (<see cref="GestorSincronizacion.ProcesarTabla"/>)
    /// pueda distinguirlo de un error de un solo registro y cortar la pasada: si el servicio no contesta a uno, tampoco a los demás.
    /// </summary>
    public class ServicioExternoSinRespuestaException : TimeoutException
    {
        public ServicioExternoSinRespuestaException(string mensaje, Exception interna = null) : base(mensaje, interna)
        {
        }
    }

    /// <summary>
    /// NestoAPI#820 (incidente 04-05/11/26): espera una llamada externa con un tiempo máximo. El 04/11 a las 19:55
    /// <c>PublishAsync</c> de Pub/Sub se quedó esperando para siempre (sin timeout ni cancelación) y con él el único worker de la
    /// cola «default» de Hangfire durante 14 horas. Se cancela el token al vencer Y, además, se deja de esperar aunque la llamada
    /// no haga caso del token (una conexión gRPC colgada puede no hacerlo): el job falla con un mensaje claro y ELMAH lo ve.
    /// </summary>
    public static class EsperaConTimeout
    {
        /// <param name="servicio">Quién no contesta («Pub/Sub»).</param>
        /// <param name="accion">Qué se le pedía, para el mensaje («al publicar en el topic sincronizacion-productos»).</param>
        public static async Task Ejecutar(Func<CancellationToken, Task> operacion, TimeSpan tiempoMaximo, string servicio, string accion)
        {
            if (operacion == null)
            {
                throw new ArgumentNullException(nameof(operacion));
            }
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(tiempoMaximo);
                Task tarea;
                try
                {
                    tarea = operacion(cts.Token);
                }
                catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
                {
                    throw SinRespuesta(servicio, accion, tiempoMaximo, ex);
                }
                // Task.Delay con el mismo token: termina (cancelada) justo cuando vence el plazo.
                Task plazo = Task.Delay(Timeout.Infinite, cts.Token);
                Task primera = await Task.WhenAny(tarea, plazo).ConfigureAwait(false);
                if (primera != tarea)
                {
                    // La llamada sigue viva por su cuenta: que su error, si llega, no quede sin observar.
                    _ = tarea.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                    throw SinRespuesta(servicio, accion, tiempoMaximo, null);
                }
                try
                {
                    await tarea.ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
                {
                    throw SinRespuesta(servicio, accion, tiempoMaximo, ex);
                }
            }
        }

        internal static ServicioExternoSinRespuestaException SinRespuesta(string servicio, string accion, TimeSpan tiempoMaximo, Exception interna)
        {
            string queHacia = string.IsNullOrWhiteSpace(accion) ? string.Empty : " " + accion.Trim();
            return new ServicioExternoSinRespuestaException(
                $"{servicio} no ha contestado en {TextoPlazo(tiempoMaximo)}{queHacia}: se cancela la llamada para no dejar parado el job (NestoAPI#820).", interna);
        }

        internal static string TextoPlazo(TimeSpan plazo)
        {
            return plazo.TotalSeconds >= 1
                ? $"{Math.Round(plazo.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture)} s"
                : $"{Math.Round(plazo.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture)} ms";
        }
    }
}
