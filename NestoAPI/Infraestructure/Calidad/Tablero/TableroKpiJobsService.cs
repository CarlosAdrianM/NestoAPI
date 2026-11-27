using Hangfire;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using System;

namespace NestoAPI.Infraestructure.Calidad.Tablero
{
    public interface IProgramadorTablero
    {
        string Encolar(int semanas, string usuario);
        string Programar(DateTime cuandoMadrid, int semanas, string usuario);
    }

    public class ProgramadorTableroHangfire : IProgramadorTablero
    {
        public string Encolar(int semanas, string usuario) => BackgroundJob.Enqueue(() => TableroKpiJobsService.Recalcular(semanas, usuario));

        public string Programar(DateTime cuandoMadrid, int semanas, string usuario)
        {
            DateTime utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(cuandoMadrid, DateTimeKind.Unspecified), Startup.ZonaHorariaPeninsular());
            return BackgroundJob.Schedule(() => TableroKpiJobsService.Recalcular(semanas, usuario), new DateTimeOffset(utc, TimeSpan.Zero));
        }
    }

    /// <summary>
    /// NestoAPI#914: el job semanal del tablero de KPI (cola «indicadores», los lunes a las 4:20 de Madrid). Calcula las semanas ya maduras que
    /// faltan (de las últimas <see cref="ServicioTablero.SEMANAS_BUSCAR_JOB"/>) y recalcula siempre las últimas
    /// <see cref="ServicioTablero.SEMANAS_RECALCULAR_JOB"/> cerradas. Sin Scripts/Issue914t_CalidadTablero.sql no hace nada (ELMAH una vez).
    /// </summary>
    public static class TableroKpiJobsService
    {
        public const string JOB_ID = "calidad-tablero-kpi";
        public const string CRON = "20 4 * * 1";
        public const string COLA = IndicadoresStock.IndicadoresStockJobsService.COLA;
        public const string SCRIPT = "Scripts/Issue914t_CalidadTablero.sql";
        public const string MENSAJE_SIN_TABLAS = "Todavía no existen dbo.CalidadKpiSemanas y dbo.CalidadKpiUmbrales: hay que lanzar " + SCRIPT + ".";
        internal const string USUARIO_ELMAH = "Sistema (tablero de KPI)";

        private static bool sinTablasAvisado;

        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 3600)]
        public static void CalcularSemanal()
        {
            DateTime madrid = FuentesKpiSql.AhoraMadrid();
            if (HorarioHistoricoCorreos.EnHorario(madrid))
            {
                // La cola iba con retraso y ya es horario: a la noche, para no cargar la BD con la gente trabajando.
                _ = new ProgramadorTableroHangfire().Programar(HorarioHistoricoCorreos.CuandoEmpezar(madrid), ServicioTablero.SEMANAS_BUSCAR_JOB, null);
                return;
            }
            _ = Ejecutar(ServicioTablero.SEMANAS_BUSCAR_JOB, ServicioTablero.SEMANAS_RECALCULAR_JOB, "job semanal");
        }

        /// <summary>Lo que encola (o programa para la noche) POST api/Calidad/Tablero/Recalcular: recalcula TODAS las semanas cerradas de las últimas N.</summary>
        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 3600)]
        public static void Recalcular(int semanas, string usuario)
        {
            int n = Math.Max(1, Math.Min(ServicioTablero.SEMANAS_MAXIMAS, semanas));
            _ = Ejecutar(n, n, string.IsNullOrWhiteSpace(usuario) ? "a mano" : $"a mano ({usuario})");
        }

        internal static ResultadoCalculoTablero Ejecutar(int semanasBuscar, int semanasRecalcular, string origen, IRepositorioTablero repositorio = null,
            Func<IFuentesKpi> fuentes = null, Action<Exception> registrar = null)
        {
            Action<Exception> log = registrar ?? (ex => ElmahHelper.Log(ex, USUARIO_ELMAH));
            try
            {
                IRepositorioTablero repo = repositorio ?? RepositorioTableroSql.DesdeConfiguracion();
                if (!repo.TablasExisten())
                {
                    if (!sinTablasAvisado)
                    {
                        sinTablasAvisado = true;
                        log(new Exception($"[Tablero #914] {MENSAJE_SIN_TABLAS} El job ({origen}) no hace nada."));
                    }
                    return new ResultadoCalculoTablero();
                }
                return new ServicioTablero(repo, fuentes ?? (() => FuentesKpiSql.DesdeConfiguracion()), registrar: log).CalcularSemanas(semanasBuscar, semanasRecalcular);
            }
            catch (Exception ex)
            {
                log(new Exception($"[Tablero #914] El cálculo de las semanas ({origen}) ha fallado: {ex.Message}", ex));
                return new ResultadoCalculoTablero { Fallos = 1 };
            }
        }

        /// <summary>Para los tests.</summary>
        internal static void OlvidarAvisoSinTablas() => sinTablasAvisado = false;
    }
}
