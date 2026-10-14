using Hangfire;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.IndicadoresStock
{
    public enum ResultadoMesIndicadoresStock
    {
        /// <summary>Guardado con todos los indicadores.</summary>
        Completo,
        /// <summary>Guardado, pero alguna consulta falló y sus indicadores quedan a null (está en ELMAH).</summary>
        Parcial,
        /// <summary>No se ha guardado nada (sin tabla, sin productos o error al guardar; está en ELMAH).</summary>
        Error
    }

    /// <summary>
    /// NestoAPI#629 (corte 1 de la épica #628): rellena la tabla IndicadoresStock.
    /// <list type="bullet">
    /// <item><b>indicadores-stock-mensual</b>: el día 1 a las 03:30 rellena el mes anterior (si el día 1 es domingo, el día 2:
    /// la madrugada del domingo corre 5 horas el SP de precios medios). A las 03:30 ya han terminado los jobs de la 1:00 a las
    /// 3:00 (campañas, sentinel, stocks, precios medios incremental, clientes nocturno).</item>
    /// <item><b>Reconstrucción</b> (POST api/IndicadoresStock/Reconstruir?meses=36): un mes por iteración, del más reciente al más
    /// antiguo. Solo trabaja de noche (de <see cref="HORA_INICIO_NOCHE"/>:00 a <see cref="HORA_FIN_NOCHE"/>:00): si se encola de
    /// día, o se le hace de día a mitad, se reprograma para las <see cref="HORA_INICIO_NOCHE"/>:00 con los meses que le faltan.</item>
    /// </list>
    /// Cada mes es idempotente: se borra y se vuelve a insertar en una transacción, así que relanzar no duplica. Las consultas
    /// recorren ExtractoProducto y LinPedidoVta enteras, con NOLOCK y un tope de <see cref="RepositorioIndicadoresStockSql.SEGUNDOS_CONSULTA_JOB"/>
    /// segundos cada una; si una falla, se apunta en ELMAH y sus indicadores quedan a null, sin tirar el resto ni los otros meses.
    /// Va en su propia cola (<see cref="COLA"/>, que atiende el servidor de un hilo de los jobs largos de Startup) para no dejar
    /// parados los jobs de cada 5 minutos.
    /// </summary>
    public class IndicadoresStockJobsService
    {
        public const string ID_JOB_MENSUAL = "indicadores-stock-mensual";
        /// <summary>Días 1 y 2 a las 03:30: el job sale si no le toca (<see cref="TocaMensual"/>).</summary>
        public const string CRON_MENSUAL = "30 3 1,2 * *";
        /// <summary>Cola propia; la atiende el servidor de un hilo de los jobs largos (el del reentrenamiento del modelo de llamadas, Startup).</summary>
        public const string COLA = "indicadores";
        public const int MESES_RECONSTRUCCION = 36;
        public const int MAXIMO_MESES_RECONSTRUCCION = 60;
        internal const int HORA_INICIO_NOCHE = 22;
        internal const int HORA_FIN_NOCHE = 7;
        private const string PREFIJO = "[Indicadores de stock #629]";

        private readonly IRepositorioIndicadoresStock repositorio;
        private readonly Func<DateTime> ahora;
        private readonly Action<Exception> registrarError;
        private readonly Action<int, DateTime, DateTime> programar;

        /// <param name="programar">(meses, desde, cuándo): reprograma la reconstrucción de los meses que faltan.</param>
        internal IndicadoresStockJobsService(IRepositorioIndicadoresStock repositorio, Func<DateTime> ahora = null,
            Action<Exception> registrarError = null, Action<int, DateTime, DateTime> programar = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.ahora = ahora ?? (() => DateTime.Now);
            this.registrarError = registrarError ?? ElmahHelper.Log;
            this.programar = programar ?? ((meses, desde, cuando) =>
                BackgroundJob.Schedule(() => Reconstruir(meses, desde), new DateTimeOffset(cuando)));
        }

        /// <summary>Punto de entrada del job recurrente (días 1 y 2 a las 03:30).</summary>
        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 3600)]
        public static async Task Mensual()
        {
            using (var db = new NVEntities())
            {
                _ = await new IndicadoresStockJobsService(new RepositorioIndicadoresStockSql(db, RepositorioIndicadoresStockSql.SEGUNDOS_CONSULTA_JOB))
                    .ProcesarMensual().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Lo que encola POST api/IndicadoresStock/Reconstruir (y lo que se reprograma a sí mismo): <paramref name="meses"/> meses
        /// empezando por <paramref name="desde"/> y hacia atrás.
        /// </summary>
        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 3600)]
        public static async Task Reconstruir(int meses, DateTime desde)
        {
            using (var db = new NVEntities())
            {
                _ = await new IndicadoresStockJobsService(new RepositorioIndicadoresStockSql(db, RepositorioIndicadoresStockSql.SEGUNDOS_CONSULTA_JOB))
                    .ProcesarReconstruccion(meses, desde).ConfigureAwait(false);
            }
        }

        /// <summary>El día 1, salvo que sea domingo; entonces el día 2.</summary>
        internal static bool TocaMensual(DateTime ahora)
        {
            return (ahora.Day == 1 && ahora.DayOfWeek != DayOfWeek.Sunday)
                || (ahora.Day == 2 && ahora.DayOfWeek == DayOfWeek.Monday);
        }

        internal static bool EsDeNoche(DateTime ahora) => ahora.Hour >= HORA_INICIO_NOCHE || ahora.Hour < HORA_FIN_NOCHE;

        /// <summary>Cuándo empieza la próxima noche: ya si es de noche; si no, hoy a las <see cref="HORA_INICIO_NOCHE"/>:00.</summary>
        internal static DateTime ProximaNoche(DateTime ahora) => EsDeNoche(ahora) ? ahora : ahora.Date.AddHours(HORA_INICIO_NOCHE);

        /// <summary>El mes anterior al de <paramref name="ahora"/> (día 1).</summary>
        internal static DateTime MesAnterior(DateTime ahora) => new DateTime(ahora.Year, ahora.Month, 1).AddMonths(-1);

        /// <summary>Si le toca, rellena el mes anterior (si por lo que sea arranca de día, lo deja para la noche). Null si no toca.</summary>
        internal async Task<ResultadoMesIndicadoresStock?> ProcesarMensual()
        {
            DateTime momento = ahora();
            if (!TocaMensual(momento))
            {
                return null;
            }
            DateTime mes = MesAnterior(momento);
            if (!EsDeNoche(momento))
            {
                programar(1, mes, ProximaNoche(momento));
                return null;
            }
            return await RellenarMes(mes).ConfigureAwait(false);
        }

        /// <summary>
        /// Rellena <paramref name="meses"/> meses desde <paramref name="desde"/> hacia atrás, el más reciente primero. Si se hace de
        /// día, programa el resto para la noche y para. Devuelve los meses procesados en esta pasada (bien o con error).
        /// </summary>
        internal async Task<List<DateTime>> ProcesarReconstruccion(int meses, DateTime desde)
        {
            var procesados = new List<DateTime>();
            DateTime primero = RepositorioIndicadoresStockSql.PrimeroDeMes(desde);
            int total = Math.Max(0, Math.Min(meses, MAXIMO_MESES_RECONSTRUCCION));
            for (int i = 0; i < total; i++)
            {
                DateTime mes = primero.AddMonths(-i);
                DateTime momento = ahora();
                if (!EsDeNoche(momento))
                {
                    programar(total - i, mes, ProximaNoche(momento));
                    return procesados;
                }
                ResultadoMesIndicadoresStock resultado = await RellenarMes(mes).ConfigureAwait(false);
                procesados.Add(mes);
                if (resultado == ResultadoMesIndicadoresStock.Error && !await ExisteTablaSinLanzar().ConfigureAwait(false))
                {
                    // Sin tabla no tiene sentido seguir: ya está en ELMAH
                    return procesados;
                }
            }
            return procesados;
        }

        /// <summary>Calcula y guarda (borrando antes) un mes. Nunca lanza: los errores van a ELMAH.</summary>
        internal async Task<ResultadoMesIndicadoresStock> RellenarMes(DateTime mes)
        {
            mes = RepositorioIndicadoresStockSql.PrimeroDeMes(mes);
            string periodo = mes.ToString("yyyy-MM");
            try
            {
                if (!await repositorio.ExisteTabla().ConfigureAwait(false))
                {
                    registrarError(new Exception($"{PREFIJO} No existe la tabla IndicadoresStock: falta lanzar Scripts/Issue629_IndicadoresStock.sql. No se ha calculado {periodo}."));
                    return ResultadoMesIndicadoresStock.Error;
                }

                IList<ProductoIndicadorStock> productos = await repositorio.LeerProductos().ConfigureAwait(false);
                bool completo = true;
                async Task<IList<T>> Leer<T>(string que, Func<Task<List<T>>> consulta)
                {
                    try
                    {
                        return await consulta().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        completo = false;
                        registrarError(new Exception($"{PREFIJO} {periodo}: falló la consulta de {que}; esos indicadores quedan vacíos. {ex.Message}", ex));
                        return null;
                    }
                }

                var datos = new DatosIndicadoresStock
                {
                    Mes = mes,
                    Productos = productos,
                    Stock = await Leer("stock", () => repositorio.LeerStock(mes)).ConfigureAwait(false),
                    Ventas = await Leer("ventas", () => repositorio.LeerVentas(mes)).ConfigureAwait(false),
                    Servicio = await Leer("días en servir", () => repositorio.LeerServicio(mes)).ConfigureAwait(false),
                    MovimientosComprados = await Leer("compras con cobertura", () => repositorio.LeerMovimientosProductosComprados(mes)).ConfigureAwait(false)
                };
                List<FilaIndicadoresStock> filas = CalculadoraIndicadoresStock.Calcular(datos);
                await repositorio.Guardar(mes, filas).ConfigureAwait(false);
                return completo ? ResultadoMesIndicadoresStock.Completo : ResultadoMesIndicadoresStock.Parcial;
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"{PREFIJO} No se ha podido calcular {periodo}: {ex.Message}", ex));
                return ResultadoMesIndicadoresStock.Error;
            }
        }

        private async Task<bool> ExisteTablaSinLanzar()
        {
            try
            {
                return await repositorio.ExisteTabla().ConfigureAwait(false);
            }
            catch
            {
                return true;
            }
        }
    }
}
