using Hangfire;
using ModeloSugerenciasProductos.Datos;
using ModeloSugerenciasProductos.Entrenamiento;
using ModeloSugerenciasProductos.Evaluacion;
using ModeloSugerenciasProductos.Features;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text;

namespace NestoAPI.Infraestructure.SugerenciasProductos
{
    /// <summary>NestoAPI#671 (corte 2d): lo que hace falta del núcleo para un entrenamiento (sustituible en los tests).</summary>
    public interface IEntrenadorModeloSugerencias
    {
        /// <summary>Lee la BD (la ventana de <paramref name="opciones"/>) y entrena, mide y decide sin guardar nada.</summary>
        IEvaluacionModeloSugerencias Evaluar(OpcionesEntrenamiento opciones, DateTime hoy, Action<string> log);
    }

    public interface IEvaluacionModeloSugerencias
    {
        bool DatosSuficientes { get; }
        DecisionPuerta Decision { get; }
        /// <summary>Métricas del modelo (con la diversidad) en la validación; null si no hay.</summary>
        ResultadoRanking Modelo { get; }
        /// <summary>Métricas de reglas-1 en la validación; null si no hay.</summary>
        ResultadoRanking Reglas { get; }
        /// <summary>NestoAPI#683: el mismo modelo con las features de la versión 1 (misma partición); null si no hay.</summary>
        ResultadoRanking SinEstado { get; }
        string InformeMetricas();
        void EntrenarFinalYGuardar(string ruta);
        string Readme(string rutaModelo, string comoEntrenar);
        MetadatosModelo Metadatos(DateTime fecha, string origen, string rutaModelo);
    }

    /// <summary>
    /// El de verdad: <see cref="RepositorioDatosSql"/> con NestoConnection (las compras, en UNA consulta con NOLOCK y READ
    /// UNCOMMITTED, la misma que la consola y el job nocturno; y las fichas) y <see cref="ProcesoEntrenamiento"/>.
    /// </summary>
    public class EntrenadorModeloSugerencias : IEntrenadorModeloSugerencias
    {
        public IEvaluacionModeloSugerencias Evaluar(OpcionesEntrenamiento opciones, DateTime hoy, Action<string> log)
        {
            string cadena = ConfigurationManager.ConnectionStrings["NestoConnection"].ConnectionString;
            VentanaEntrenamiento ventana = VentanaEntrenamiento.Calcular(hoy, opciones);
            // Las compras crudas solo viven aquí: en cuanto el proceso construye las instantáneas, quedan para el GC.
            DatosCrudos datos = new RepositorioDatosSql(() => new SqlConnection(cadena)).Leer(ventana.DatosDesde, ventana.DatosHasta, log);
            log?.Invoke($"Datos: {datos.Compras.Count} compras (cliente, producto, día) y {datos.Fichas.Count} fichas.");
            return new Evaluacion(ProcesoEntrenamiento.Evaluar(datos, opciones, hoy, log));
        }

        /// <summary>Envuelve un <see cref="ProcesoEntrenamiento"/> ya evaluado (internal: el test que entrena de verdad en .NET Framework).</summary>
        internal sealed class Evaluacion : IEvaluacionModeloSugerencias
        {
            private readonly ProcesoEntrenamiento proceso;

            public Evaluacion(ProcesoEntrenamiento proceso)
            {
                this.proceso = proceso;
            }

            public bool DatosSuficientes => proceso.DatosSuficientes;
            public DecisionPuerta Decision => proceso.Decision;
            public ResultadoRanking Modelo => proceso.Modelo;
            public ResultadoRanking Reglas => proceso.Reglas;
            public ResultadoRanking SinEstado => proceso.SinEstado;
            public string InformeMetricas() => proceso.InformeMetricas();
            public void EntrenarFinalYGuardar(string ruta) => _ = proceso.EntrenarFinalYGuardar(ruta);
            public string Readme(string rutaModelo, string comoEntrenar) => proceso.Readme(rutaModelo, comoEntrenar);
            public MetadatosModelo Metadatos(DateTime fecha, string origen, string rutaModelo) => proceso.Metadatos(fecha, origen, rutaModelo);
        }
    }

    public enum EstadoEntrenamientoSugerencias
    {
        /// <summary>Entrenado y guardado; pasa la puerta: desde el corte 2c, Calcular lo usa en cuanto lo ve (PredictorSugerenciasProductos).</summary>
        Pasaria,
        /// <summary>Entrenado y guardado; no pasaría la puerta.</summary>
        NoPasaria,
        DatosInsuficientes,
        /// <summary>De lunes a viernes de 7:00 a 21:00 y sin forzar: no se entrena.</summary>
        EnHorario,
        Error
    }

    /// <summary>
    /// NestoAPI#671 (corte 2d): dónde deja el job lo que entrena. Fuera de la carpeta publicada (por defecto
    /// <see cref="CARPETA_POR_DEFECTO"/>, appSetting <see cref="CLAVE_CARPETA"/>) para que un deploy no lo pise, y con
    /// histórico: cada ejecución deja sus ficheros con el sufijo AAAAMMDD_HHmm (zip, json, README, informe y log), como la consola.
    /// NestoAPI#671 (corte 2c): no hay «modelo activo» aparte: Calcular usa el último zip cuyo json dice PasaPuerta (PredictorSugerenciasProductos); se apaga con el parámetro ModeloSugerenciasActivo = 0 de «(defecto)».
    /// </summary>
    public class AlmacenModeloSugerencias
    {
        internal const string CLAVE_CARPETA = "ModeloSugerencias:Carpeta";
        internal const string CARPETA_POR_DEFECTO = @"C:\NestoAPI\ModelosIA\Sugerencias";

        public AlmacenModeloSugerencias(string carpeta)
        {
            Carpeta = carpeta;
        }

        public static AlmacenModeloSugerencias Produccion()
            => new AlmacenModeloSugerencias(CarpetaConfigurada(clave => ConfigurationManager.AppSettings[clave]));

        internal static string CarpetaConfigurada(Func<string, string> leerParametro)
        {
            string valor = leerParametro(CLAVE_CARPETA);
            return string.IsNullOrWhiteSpace(valor) ? CARPETA_POR_DEFECTO : valor.Trim();
        }

        public string Carpeta { get; }

        public static string Sufijo(DateTime fecha) => fecha.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
        public string RutaModelo(string sufijo) => Path.Combine(Carpeta, $"modelo_sugerencias_productos_{sufijo}.zip");
        public string RutaMetadatos(string sufijo) => Path.Combine(Carpeta, $"modelo_sugerencias_productos_{sufijo}.json");
        public string RutaReadme(string sufijo) => Path.Combine(Carpeta, $"README_modelo_sugerencias_productos_{sufijo}.md");
        public string RutaInforme(string sufijo) => Path.Combine(Carpeta, $"informe_sugerencias_{sufijo}.md");
        public string RutaLog(string sufijo) => Path.Combine(Carpeta, "logs", $"entrenamiento_sugerencias_{sufijo}.log");

        public void Escribir(string ruta, string texto, bool bom = true)
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(ruta));
            File.WriteAllText(ruta, texto ?? "", new UTF8Encoding(bom));
        }
    }

    /// <summary>
    /// NestoAPI#671 (corte 2d): entrenamiento mensual del modelo 1 de sugerencias de productos (propensión de compra) DENTRO de
    /// la API, como el de llamadas (<see cref="ReentrenamientoModeloLlamadasJobsService"/>), con el núcleo
    /// SugerenciasProductos.Nucleo y las opciones por defecto (las de la consola con --completo: todos los clientes, 36 meses).
    /// <para>Job «entrenar-modelo-sugerencias»: el SEGUNDO sábado de cada mes a las 02:30 (el primero es el de llamadas). El cron
    /// de Hangfire no sabe de «segundo sábado»: corre todos los sábados y el job sale si no toca (<see cref="TocaEntrenar"/>).
    /// Cola «entrenamiento» (servidor de Hangfire de un hilo, Startup), así que nunca coincide con el de llamadas ni deja
    /// parados los jobs de la cola «default».</para>
    /// <para>Guarda zip, json, README e informe frente a reglas-1 en <see cref="AlmacenModeloSugerencias"/>, con histórico por
    /// fecha. Si pasa la puerta, Calcular lo usa solo (corte 2c). Avisa en la campana de Nesto (buzón + SignalR) a <see cref="CLAVE_USUARIOS_AVISO"/>
    /// (por defecto Carlos): si pasaría la puerta, las métricas y dónde está el informe. Los errores, además, a ELMAH. Sin
    /// reintentos automáticos (un reintento caería en horario).</para>
    /// <para>Guarda horaria: de lunes a viernes de 7:00 a 21:00 no entrena (lee 36 meses de LinPedidoVta y ocupa la CPU un buen
    /// rato); lo apunta, avisa y sale. Solo se salta con <c>forzar</c> (POST api/SugerenciasProductos/ReentrenarModelo?forzar=true).</para>
    /// </summary>
    public class ReentrenamientoModeloSugerenciasJobsService
    {
        public const string ID_JOB = "entrenar-modelo-sugerencias";
        /// <summary>Todos los sábados a las 02:30; el job sale si no es el segundo del mes.</summary>
        public const string CRON = "30 2 * * 6";
        public const string COLA = ReentrenamientoModeloLlamadasJobsService.COLA;
        internal const int HORA_INICIO_HORARIO = 7;
        internal const int HORA_FIN_HORARIO = 21;
        /// <summary>Hora a la que se programa lo que se pide en horario sin decir cuándo.</summary>
        internal static readonly TimeSpan HORA_FRANJA_NOCHE = new TimeSpan(23, 30, 0);
        /// <summary>De lunes a viernes, lo que se pide a partir de esta hora (y antes de las 7) espera a la noche: no daría tiempo.</summary>
        internal const int HORA_LIMITE_MADRUGADA = 5;
        internal const string CLAVE_USUARIOS_AVISO = "ModeloSugerencias:UsuariosAviso";
        internal const string USUARIOS_AVISO_POR_DEFECTO = "Carlos";
        internal const string TITULO_AVISO = "Modelo de sugerencias";
        internal const string ORIGEN_PROGRAMADO = "job mensual";

        internal const string COMO_ENTRENAR = "Lo hace el job «entrenar-modelo-sugerencias» de NestoAPI el segundo sábado de cada mes a " +
            "las 02:30 (cola «entrenamiento» de Hangfire). A mano: `POST api/SugerenciasProductos/ReentrenarModelo` (Dirección o " +
            "Informática): sin `cuando`, se programa para la próxima franja permitida (de lunes a viernes, a las 23:30); con " +
            "`cuando=AAAA-MM-DDTHH:mm`, a esa hora (fuera de horario). Deja zip, json, README e informe con la fecha en " +
            "appSetting ModeloSugerencias:Carpeta (por defecto C:\\NestoAPI\\ModelosIA\\Sugerencias). Si pasa la puerta, Calcular lo usa solo (corte 2c; para volver a las reglas, ParametrosUsuario «(defecto)» ModeloSugerenciasActivo = 0). " +
            "También desde la consola ModeloSugerenciasProductos (`--completo --simular`).";

        private readonly IEntrenadorModeloSugerencias entrenador;
        private readonly AlmacenModeloSugerencias almacen;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<DateTime> ahora;
        private readonly Func<string, string> leerParametro;
        private readonly Action asegurarNativo;
        private readonly Action<Exception> registrarError;
        private readonly Action liberarMemoria;

        public ReentrenamientoModeloSugerenciasJobsService() : this(null)
        {
        }

        internal ReentrenamientoModeloSugerenciasJobsService(IEntrenadorModeloSugerencias entrenador, AlmacenModeloSugerencias almacen = null,
            IServicioNotificacionesPush notificaciones = null, Func<DateTime> ahora = null, Func<string, string> leerParametro = null,
            Action asegurarNativo = null, Action<Exception> registrarError = null, Action liberarMemoria = null)
        {
            this.entrenador = entrenador ?? new EntrenadorModeloSugerencias();
            this.almacen = almacen ?? AlmacenModeloSugerencias.Produccion();
            this.notificaciones = notificaciones ?? new ServicioNotificacionesPush();
            this.ahora = ahora ?? (() => DateTime.Now);
            this.leerParametro = leerParametro ?? (clave => ConfigurationManager.AppSettings[clave]);
            this.asegurarNativo = asegurarNativo ?? (() => _ = CargadorLightGbmNativo.Asegurar());
            this.registrarError = registrarError ?? ElmahHelper.Log;
            this.liberarMemoria = liberarMemoria ?? LiberarMemoria;
        }

        /// <summary>Punto de entrada del job recurrente (todos los sábados; solo hace algo el segundo del mes de madrugada).</summary>
        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public static void EntrenarProgramado()
        {
            var servicio = new ReentrenamientoModeloSugerenciasJobsService();
            if (!TocaEntrenar(servicio.ahora()))
            {
                return;
            }
            _ = servicio.Ejecutar(forzar: false, ORIGEN_PROGRAMADO);
        }

        /// <summary>Lo que encola o programa POST api/SugerenciasProductos/ReentrenarModelo.</summary>
        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public static void ReentrenarManual(bool forzar, string usuario)
        {
            _ = new ReentrenamientoModeloSugerenciasJobsService().Ejecutar(forzar,
                string.IsNullOrWhiteSpace(usuario) ? "a mano" : $"a mano ({usuario})");
        }

        /// <summary>Segundo sábado del mes (día 8 a 14) y de madrugada (antes de las 7: si arranca más tarde, sería casi en horario).</summary>
        internal static bool TocaEntrenar(DateTime momento)
            => momento.DayOfWeek == DayOfWeek.Saturday && momento.Day >= 8 && momento.Day <= 14 && momento.Hour < HORA_INICIO_HORARIO;

        /// <summary>Horario de trabajo: de lunes a viernes, de 7:00 a 21:00 (lo mismo que la consola).</summary>
        public static bool EnHorario(DateTime momento)
            => momento.DayOfWeek != DayOfWeek.Saturday && momento.DayOfWeek != DayOfWeek.Sunday
               && momento.Hour >= HORA_INICIO_HORARIO && momento.Hour < HORA_FIN_HORARIO;

        /// <summary>
        /// Cuándo lanzar lo que se pide sin decir hora: ya, si es fin de semana o de lunes a viernes de 21:00 a 5:00; si no, hoy
        /// a las 23:30 (de lunes a viernes de 5:00 a 21:00: en horario o sin tiempo para acabar antes de las 7).
        /// </summary>
        public static DateTime ProximaFranja(DateTime momento)
        {
            bool laborable = momento.DayOfWeek != DayOfWeek.Saturday && momento.DayOfWeek != DayOfWeek.Sunday;
            return laborable && momento.Hour >= HORA_LIMITE_MADRUGADA && momento.Hour < HORA_FIN_HORARIO
                ? momento.Date + HORA_FRANJA_NOCHE
                : momento;
        }

        internal static string MotivoEnHorario(DateTime momento)
            => $"son las {momento:HH:mm} del {momento.ToString("dddd", CultureInfo.GetCultureInfo("es-ES"))} y el entrenamiento lee 36 meses " +
               "de LinPedidoVta: no se lanza de lunes a viernes de 7:00 a 21:00 (para lanzarlo igualmente, forzar=true)";

        /// <summary>Entrena, mide frente a reglas-1, guarda y avisa. Nunca lanza.</summary>
        public EstadoEntrenamientoSugerencias Ejecutar(bool forzar, string origen)
        {
            DateTime inicio = ahora();
            if (!forzar && EnHorario(inicio))
            {
                string motivo = MotivoEnHorario(inicio);
                System.Diagnostics.Trace.TraceWarning($"[Modelo de sugerencias] No se entrena ({origen}): {motivo}.");
                Avisar(TextoAviso(EstadoEntrenamientoSugerencias.EnHorario, motivo, null, null, null));
                return EstadoEntrenamientoSugerencias.EnHorario;
            }

            string sufijo = AlmacenModeloSugerencias.Sufijo(inicio);
            var log = new StringBuilder();
            void Apuntar(string linea) => log.AppendLine($"{ahora():HH:mm:ss} {linea}");
            Apuntar($"Entrenamiento del modelo de sugerencias de productos (NestoAPI#671), {origen}{(forzar ? ", forzado" : "")}.");
            try
            {
                asegurarNativo();
                // NestoAPI#683: el que usa ahora Calcular (el último que pasa la puerta), para compararlo en el informe y en el aviso
                Tuple<string, MetadatosModelo> anterior = ModeloAnterior();
                IEvaluacionModeloSugerencias evaluacion = entrenador.Evaluar(new OpcionesEntrenamiento(), inicio.Date, Apuntar);

                string rutaInforme = almacen.RutaInforme(sufijo);
                string comparacion = anterior == null ? null : ComparacionAnterior(evaluacion.Modelo, evaluacion.Reglas, evaluacion.SinEstado, anterior.Item1, anterior.Item2);
                almacen.Escribir(rutaInforme, evaluacion.InformeMetricas() + (comparacion == null ? string.Empty : Environment.NewLine + comparacion));
                if (comparacion != null)
                {
                    Apuntar(comparacion);
                }
                if (!evaluacion.DatosSuficientes)
                {
                    Avisar(TextoAviso(EstadoEntrenamientoSugerencias.DatosInsuficientes, evaluacion.Decision?.Motivo, null, null, rutaInforme));
                    return EstadoEntrenamientoSugerencias.DatosInsuficientes;
                }

                string rutaModelo = almacen.RutaModelo(sufijo);
                evaluacion.EntrenarFinalYGuardar(rutaModelo);
                almacen.Escribir(almacen.RutaMetadatos(sufijo),
                    JsonConvert.SerializeObject(evaluacion.Metadatos(ahora(), origen, rutaModelo), Formatting.Indented), bom: false);
                almacen.Escribir(almacen.RutaReadme(sufijo), evaluacion.Readme(rutaModelo, COMO_ENTRENAR));

                EstadoEntrenamientoSugerencias estado = evaluacion.Decision != null && evaluacion.Decision.Pasa
                    ? EstadoEntrenamientoSugerencias.Pasaria
                    : EstadoEntrenamientoSugerencias.NoPasaria;
                Apuntar($"Terminado: {(estado == EstadoEntrenamientoSugerencias.Pasaria ? "pasaría" : "no pasaría")} la puerta.");
                Avisar(TextoAviso(estado, evaluacion.Decision?.Motivo, evaluacion.Modelo, evaluacion.Reglas, rutaInforme)
                    + (anterior == null ? string.Empty : $" Modelo anterior ({anterior.Item1}): P@3 {Pct(anterior.Item2.P3)} en su validación."));
                return estado;
            }
            catch (Exception ex)
            {
                Apuntar($"ERROR: {ex}");
                registrarError(new Exception($"[Modelo de sugerencias] Error en el entrenamiento ({origen}): {ex.Message}", ex));
                Avisar(TextoAviso(EstadoEntrenamientoSugerencias.Error, ex.Message, null, null, null));
                return EstadoEntrenamientoSugerencias.Error;
            }
            finally
            {
                try
                {
                    almacen.Escribir(almacen.RutaLog(sufijo), log.ToString());
                }
                catch (Exception)
                {
                    // El log es una ayuda; si no se puede escribir, no cambia el resultado.
                }
                liberarMemoria();
            }
        }

        /// <summary>NestoAPI#683: el último modelo que pasa la puerta en la carpeta (sea de la versión de features que sea) y su json; null si no hay.</summary>
        private Tuple<string, MetadatosModelo> ModeloAnterior()
        {
            try
            {
                ModeloSugerenciasElegido elegido = PredictorSugerenciasProductos.ElegirModelo(almacen.Carpeta);
                if (elegido == null)
                {
                    return null;
                }
                MetadatosModelo meta = JsonConvert.DeserializeObject<MetadatosModelo>(File.ReadAllText(Path.ChangeExtension(elegido.RutaModelo, ".json")));
                return meta == null ? null : Tuple.Create(Path.GetFileName(elegido.RutaModelo), meta);
            }
            catch (Exception)
            {
                return null; // La comparación es una ayuda: sin ella, el entrenamiento sigue
            }
        }

        /// <summary>
        /// NestoAPI#683: sección del informe frente al modelo que usa ahora Calcular. Sus métricas son las de SU validación (otros
        /// meses), así que la comparación justa con lo que aportan las features nuevas es «sin el estado» (misma partición).
        /// </summary>
        internal static string ComparacionAnterior(ResultadoRanking modelo, ResultadoRanking reglas, ResultadoRanking sinEstado, string ficheroAnterior, MetadatosModelo anterior)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Frente al modelo anterior (NestoAPI#683)");
            sb.AppendLine();
            string ahora = ModeloSugerenciasProductos.Evaluacion.Diversidad.APLICAR ? "con" : "sin";
            string antes = anterior.Diversidad == null ? "con" : anterior.Diversidad.Value ? "con" : "sin";
            sb.AppendLine("| Modelo | Versión de features | Validación | Diversidad | P@3 | P@3 reglas-1 | NDCG@3 |");
            sb.AppendLine("|---|---:|---|---|---:|---:|---:|");
            sb.AppendLine($"| Anterior: `{ficheroAnterior}` | {anterior.VersionFeatures} | {Mes(anterior.Corte)} – {Mes(anterior.UltimaInstantanea)} | {antes} | {Pct(anterior.P3)} | {Pct(anterior.P3Reglas)} | {Num(anterior.Ndcg3)} |");
            sb.AppendLine($"| Este | {ModeloSugerenciaEntrada.VERSION_FEATURES} | esta | {ahora} | {Pct(modelo?.P3)} | {Pct(reglas?.P3)} | {Num(modelo?.Ndcg3)} |");
            sb.AppendLine($"| Este sin el estado (features v1, misma partición) | 1 | esta | {ahora} | {Pct(sinEstado?.P3)} | {Pct(reglas?.P3)} | {Num(sinEstado?.Ndcg3)} |");
            sb.AppendLine();
            sb.AppendLine("Las métricas del anterior son las de su propia validación (otros meses y otros datos)" +
                (antes != ahora ? $" y se midieron {antes} la diversidad (este, {ahora} ella, como Calcular)" : "") +
                ": la comparación justa de lo que aportan las features nuevas es la fila «sin el estado».");
            return sb.ToString();
        }

        private static string Mes(DateTime fecha) => fecha == default ? "—" : fecha.ToString("MM/yyyy", CultureInfo.InvariantCulture);

        private static string Pct(double? v) => v == null || double.IsNaN(v.Value) ? "—" : (v.Value * 100).ToString("0.0", CultureInfo.GetCultureInfo("es-ES")) + " %";

        private static string Num(double? v) => v == null || double.IsNaN(v.Value) ? "—" : v.Value.ToString("0.000", CultureInfo.GetCultureInfo("es-ES"));

        /// <summary>
        /// 36 meses de compras, las instantáneas y los datos de LightGBM ocupan cientos de MB en el proceso de la API: al acabar
        /// (ya sin referencias) se recoge y se compacta el LOH una vez para devolver esa memoria.
        /// </summary>
        private static void LiberarMemoria()
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        internal static string TextoAviso(EstadoEntrenamientoSugerencias estado, string motivo, ResultadoRanking modelo, ResultadoRanking reglas, string rutaInforme)
        {
            switch (estado)
            {
                case EstadoEntrenamientoSugerencias.Pasaria:
                    return $"Modelo de sugerencias: entrenado, PASARÍA la puerta ({motivo}). {Metricas(modelo, reglas)} Las sugerencias ya lo usan (para volver a las reglas: parámetro ModeloSugerenciasActivo = 0 en «(defecto)»). Informe: {rutaInforme}";
                case EstadoEntrenamientoSugerencias.NoPasaria:
                    return $"Modelo de sugerencias: entrenado, NO pasaría la puerta ({motivo}). {Metricas(modelo, reglas)} Las sugerencias siguen con el último que la pasó (o con las reglas). Informe: {rutaInforme}";
                case EstadoEntrenamientoSugerencias.DatosInsuficientes:
                    return $"Modelo de sugerencias: no hay datos suficientes para entrenar y validar. Informe: {rutaInforme}";
                case EstadoEntrenamientoSugerencias.EnHorario:
                    return $"Modelo de sugerencias: no se ha entrenado: {motivo}.";
                default:
                    return $"Modelo de sugerencias: error en el entrenamiento ({motivo}). Las sugerencias siguen como estaban. Detalle en ELMAH.";
            }
        }

        private static string Metricas(ResultadoRanking modelo, ResultadoRanking reglas)
        {
            CultureInfo es = CultureInfo.GetCultureInfo("es-ES");
            string Pct(double? v) => v == null || double.IsNaN(v.Value) ? "—" : (v.Value * 100).ToString("0.0", es) + " %";
            string Num(double? v) => v == null || double.IsNaN(v.Value) ? "—" : v.Value.ToString("0.000", es);
            return $"P@3 {Pct(modelo?.P3)} (reglas-1 {Pct(reglas?.P3)}), NDCG@3 {Num(modelo?.Ndcg3)} (reglas-1 {Num(reglas?.Ndcg3)}), " +
                   $"R@3 {Pct(modelo?.R3)}, al menos 1 acierto {Pct(modelo?.AlMenos1)}.";
        }

        internal List<string> Destinatarios()
        {
            string configurados = leerParametro(CLAVE_USUARIOS_AVISO);
            return (string.IsNullOrWhiteSpace(configurados) ? USUARIOS_AVISO_POR_DEFECTO : configurados)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(u => u.Trim())
                .Where(u => u.Length > 0)
                .Select(u => u.Contains("\\") ? u : "NUEVAVISION\\" + u)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>A la campana de Nesto. Si falla el aviso, a ELMAH (el entrenamiento ya está hecho o descartado).</summary>
        private void Avisar(string texto)
        {
            var notificacion = new NotificacionPushDTO
            {
                Titulo = TITULO_AVISO,
                Cuerpo = texto,
                Tipo = NotificacionesController.TIPO_AVISO_NESTO,
                Datos = new Dictionary<string, string> { ["tipo"] = NotificacionesController.TIPO_AVISO_NESTO }
            };
            foreach (string usuario in Destinatarios())
            {
                try
                {
                    notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    registrarError(new Exception($"[Modelo de sugerencias] No se pudo avisar a {usuario}: {ex.Message}. Aviso: {texto}", ex));
                }
            }
        }
    }
}
