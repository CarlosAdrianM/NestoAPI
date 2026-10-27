using Hangfire;
using System;

namespace NestoAPI.Infraestructure.PedidosVenta.Anomalias
{
    /// <summary>
    /// NestoAPI#715: el job nocturno de Hangfire que etiqueta las filas de PedidosAnomalias (<see cref="EtiquetadorAnomalias"/>).
    /// A las 5:30, fuera de horario: lee LinPedidoVta de los pedidos pendientes de etiquetar y recorre la tabla Modificaciones de la
    /// última semana (sin índice por fecha; ~200 filas a la semana). Con el interruptor Apagado no hace nada. Nunca rompe: lo que
    /// falle va a ELMAH y no se reintenta (a la noche siguiente vuelve a intentarlo con las mismas filas).
    /// </summary>
    public static class AnomaliasJobsService
    {
        public const string ID_JOB_ETIQUETADO = "anomalias-etiquetado";
        public const string CRON_ETIQUETADO = "30 5 * * *";

        internal static Func<IRepositorioAnomaliasPedido> CrearRepositorio { get; set; } = () => RepositorioAnomaliasPedidoSql.DesdeConfiguracion();
        internal static Func<DateTime> Ahora { get; set; } = () => DateTime.Now;
        internal static Action<Exception> Registrador { get; set; } = ex => ElmahHelper.Log(ex, "Sistema (anomalías #715)");

        /// <summary>Devuelve cuántas filas ha etiquetado (0 si Apagado o sin tabla).</summary>
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static int EtiquetarNocturno()
        {
            try
            {
                if (InterruptorAnomalias.Modo == ModoAnomalias.Apagado)
                {
                    return 0;
                }
                IRepositorioAnomaliasPedido repositorio = CrearRepositorio();
                if (repositorio == null || !repositorio.TablaExiste())
                {
                    return 0;
                }
                return EtiquetadorAnomalias.EtiquetarPendientes(repositorio, Ahora()).Count;
            }
            catch (Exception ex)
            {
                Registrador(new Exception($"[Anomalías #715] Error en el etiquetado nocturno de PedidosAnomalias: {ex.Message}", ex));
                return 0;
            }
        }
    }
}
