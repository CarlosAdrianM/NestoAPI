using Hangfire;
using System;
using System.Linq;

namespace NestoAPI.Infraestructure.Abandono
{
    /// <summary>
    /// NestoAPI#714 (corte 1): los dos jobs de Hangfire del riesgo de abandono, los dos de lunes a sábado de madrugada (fuera de
    /// horario: leen 24 meses de LinPedidoVta de cada cartera, aunque por vendedor tardan menos de un segundo) y en la cola
    /// «default» (son ligeros; la cola «entrenamiento» queda para el entrenamiento del corte 2).
    /// <list type="bullet">
    /// <item><see cref="ResolverVencidas"/> («riesgo-abandono-resolver», 05:10): etiqueta las filas cuyo horizonte venció.
    /// Va ANTES de la pasada para que un cliente cuya alerta acaba de resolverse pueda volver a marcarse ese mismo día.</item>
    /// <item><see cref="PasadaDiaria"/> («riesgo-abandono-diario», 05:20): la regla baseline sobre la cartera de cada vendedor
    /// (instantánea del día anterior) y, los lunes, la instantánea semanal de todos los compradores regulares.</item>
    /// </list>
    /// Con <see cref="InterruptorAbandono"/> en Apagado o sin la tabla no hacen nada. Nunca rompen: lo que falle va a ELMAH y no se
    /// reintenta (un reintento caería en horario).
    /// </summary>
    public static class RiesgoAbandonoJobsService
    {
        public const string ID_JOB_DIARIO = "riesgo-abandono-diario";
        public const string CRON_DIARIO = "20 5 * * 1-6";
        public const string ID_JOB_RESOLVER = "riesgo-abandono-resolver";
        public const string CRON_RESOLVER = "10 5 * * 1-6";

        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static void PasadaDiaria()
        {
            try
            {
                ResumenPasadaAbandono resumen = new ServicioRiesgoAbandono().EjecutarPasada();
                System.Diagnostics.Trace.TraceInformation(Texto(resumen));
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Riesgo de abandono #714] Error en la pasada diaria: {ex.Message}", ex), "Sistema (riesgo de abandono)");
            }
        }

        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static void ResolverVencidas()
        {
            try
            {
                ResumenResolucionAbandono resumen = new ServicioRiesgoAbandono().ResolverVencidas();
                System.Diagnostics.Trace.TraceInformation(Texto(resumen));
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Riesgo de abandono #714] Error al resolver las filas vencidas: {ex.Message}", ex), "Sistema (riesgo de abandono)");
            }
        }

        internal static string Texto(ResumenPasadaAbandono r)
        {
            if (r.Modo == ModoAbandono.Apagado)
            {
                return "[Riesgo de abandono #714] Apagado: no se evalúa nada.";
            }
            if (!r.TablaExiste)
            {
                return "[Riesgo de abandono #714] Falta la tabla ClientesRiesgoAbandono (Scripts/Issue714_ClientesRiesgoAbandono.sql): no se registra nada.";
            }
            return $"[Riesgo de abandono #714] Instantánea del {r.FechaInstantanea:dd/MM/yyyy} ({r.Modo}): {r.Vendedores} vendedores" +
                   (r.VendedoresConError > 0 ? $" ({r.VendedoresConError} con error)" : "") +
                   $", cartera {r.Cartera}, compradores regulares {r.CompradoresRegulares}, marcados por la regla {r.MarcadosPorLaRegla}, " +
                   $"alertas nuevas {r.AlertasNuevas}, instantáneas semanales {r.InstantaneasSemanales}, filas insertadas {r.FilasInsertadas}. " +
                   string.Join("; ", r.Detalle);
        }

        internal static string Texto(ResumenResolucionAbandono r)
        {
            if (r.Modo == ModoAbandono.Apagado)
            {
                return "[Riesgo de abandono #714] Apagado: no se resuelve nada.";
            }
            if (!r.TablaExiste)
            {
                return "[Riesgo de abandono #714] Falta la tabla ClientesRiesgoAbandono: no se resuelve nada.";
            }
            return $"[Riesgo de abandono #714] Resueltas {r.Resueltas} filas" + (r.Errores > 0 ? $" ({r.Errores} errores)" : "") +
                   (r.PorResultado.Count == 0 ? "." : ": " + string.Join(", ", r.PorResultado.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}")) + ".");
        }
    }
}
