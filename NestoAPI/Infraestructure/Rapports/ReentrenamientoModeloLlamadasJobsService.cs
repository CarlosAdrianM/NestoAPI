using Hangfire;
using ModeloLlamadaPedido.Datos;
using ModeloLlamadaPedido.Evaluacion;
using ModeloLlamadaPedido.Features;
using ModeloLlamadaPedido.Reentrenamiento;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>NestoAPI#619: lo que hace falta del núcleo para un reentrenamiento (sustituible en los tests).</summary>
    public interface IEntrenadorModeloLlamadas
    {
        /// <summary>Lee la BD (ventana de <paramref name="opciones"/>) y entrena, compara y decide sin guardar nada.</summary>
        IEvaluacionModeloLlamadas Evaluar(OpcionesReentrenamiento opciones, DateTime hoy);

        /// <summary>
        /// NestoAPI#618: lo mismo para el modelo por subgrupo (objetivo: pedido en 7 días que incluya el subgrupo). Si ya se
        /// leyó la BD para el general con la misma ventana, no se vuelve a leer.
        /// </summary>
        IEvaluacionModeloLlamadas EvaluarSubgrupo(OpcionesReentrenamiento opciones, DateTime hoy);
    }

    public interface IEvaluacionModeloLlamadas
    {
        bool DatosSuficientes { get; }
        DecisionPuerta Decision { get; }
        /// <summary>«P@20 x → y, AUC a → b».</summary>
        string Comparacion { get; }
        void EntrenarFinalYGuardar(string ruta);
        string Readme(string rutaModelo, string resultado);
        MetadatosModelo Metadatos(DateTime fecha, string origen);
    }

    /// <summary>
    /// El de verdad: <see cref="RepositorioDatosSql"/> con NestoConnection y <see cref="ProcesoReentrenamiento"/> (o
    /// <see cref="ProcesoReentrenamientoSubgrupo"/>, NestoAPI#618). La BD se lee una vez por ejecución (con lo comprado por
    /// día y subgrupo) y la usan los dos modelos.
    /// </summary>
    public class EntrenadorModeloLlamadas : IEntrenadorModeloLlamadas
    {
        private DatosCrudos datosLeidos;
        private string claveLeida;

        public IEvaluacionModeloLlamadas Evaluar(OpcionesReentrenamiento opciones, DateTime hoy)
        {
            ProcesoReentrenamiento proceso = ProcesoReentrenamiento.Evaluar(Leer(opciones, hoy), opciones, hoy);
            return new Evaluacion(proceso, proceso.DatosSuficientes, proceso.Comparacion,
                ruta => proceso.EntrenarFinalYGuardar(ruta),
                (rutaModelo, resultado) => proceso.Readme(rutaModelo, resultado, ReentrenamientoModeloLlamadasJobsService.COMO_REENTRENAR));
        }

        public IEvaluacionModeloLlamadas EvaluarSubgrupo(OpcionesReentrenamiento opciones, DateTime hoy)
        {
            ProcesoReentrenamientoSubgrupo proceso = ProcesoReentrenamientoSubgrupo.Evaluar(Leer(opciones, hoy), opciones, hoy);
            return new Evaluacion(proceso, proceso.DatosSuficientes, proceso.Comparacion,
                ruta => proceso.EntrenarFinalYGuardar(ruta),
                (rutaModelo, resultado) => proceso.Readme(rutaModelo, resultado, ReentrenamientoModeloLlamadasJobsService.COMO_REENTRENAR_SUBGRUPO));
        }

        private DatosCrudos Leer(OpcionesReentrenamiento opciones, DateTime hoy)
        {
            (DateTime desde, DateTime hasta, _) = VentanaReentrenamiento.Calcular(hoy, opciones.Completo, opciones.Meses, opciones.MesesValidacion);
            string vendedor = opciones.Completo ? null : opciones.Vendedor;
            string clave = $"{desde:yyyyMMdd}|{hasta:yyyyMMdd}|{vendedor}";
            if (datosLeidos == null || claveLeida != clave)
            {
                string cadena = ConfigurationManager.ConnectionStrings["NestoConnection"].ConnectionString;
                datosLeidos = new RepositorioDatosSql(() => new SqlConnection(cadena))
                    .Leer(desde, hasta, vendedor, CalculadoraFeatures.MESES_HISTORIAL, CalculadoraFeatures.VENTANA_POSITIVO_DIAS, incluirSubgrupos: true);
                claveLeida = clave;
            }
            return datosLeidos;
        }

        private sealed class Evaluacion : IEvaluacionModeloLlamadas
        {
            private readonly IResultadoReentrenamiento proceso;
            private readonly Func<string, List<float>> entrenarFinal;
            private readonly Func<string, string, string> readme;

            public Evaluacion(IResultadoReentrenamiento proceso, bool datosSuficientes, string comparacion,
                Func<string, List<float>> entrenarFinal, Func<string, string, string> readme)
            {
                this.proceso = proceso;
                DatosSuficientes = datosSuficientes;
                Comparacion = comparacion;
                this.entrenarFinal = entrenarFinal;
                this.readme = readme;
            }

            public bool DatosSuficientes { get; }
            public DecisionPuerta Decision => proceso.Decision;
            public string Comparacion { get; }
            public void EntrenarFinalYGuardar(string ruta) => _ = entrenarFinal(ruta);
            public string Readme(string rutaModelo, string resultado) => readme(rutaModelo, resultado);
            public MetadatosModelo Metadatos(DateTime fecha, string origen) => MetadatosModelo.Desde(proceso, fecha, origen);
        }
    }

    public enum EstadoReentrenamiento
    {
        Promovido,
        NoPromovido,
        /// <summary>Simulación y el nuevo pasaría la puerta (no se toca nada).</summary>
        Pasaria,
        Error
    }

    /// <summary>
    /// NestoAPI#619: reentrenamiento mensual del modelo de llamadas de Rapports DENTRO de la API (decisión de Carlos, 09/10/26;
    /// sustituye a la tarea del Task Scheduler + consola + commit del zip).
    /// <para>Job «reentrenar-modelo-llamadas»: el primer sábado de cada mes a las 02:30 (el domingo de madrugada corre el SP de
    /// precios medios, que bloquea LinPedidoVta). El cron de Hangfire no tiene «primer sábado»: se programa todos los sábados y
    /// el job sale si no toca (<see cref="TocaReentrenar"/>). Corre en su propia cola (<see cref="COLA"/>, servidor de Hangfire
    /// con un solo hilo en Startup) para no tener parados una hora los jobs de cada 5 minutos.</para>
    /// <para>Entrena con la ventana de 3 años y la misma puerta de calidad que la consola (núcleo ModeloLlamadaPedido.Nucleo):
    /// solo con los contactos que el modelo activo no vio; la P@20 no puede bajar más de 1 punto y el AUC no puede bajar
    /// (appSettings <see cref="CLAVE_MAX_BAJADA_PRECISION"/> en puntos y <see cref="CLAVE_MAX_BAJADA_AUC"/>; si no están, los
    /// valores por defecto del núcleo, porque las claves puestas a mano en el Web.config del servidor se pierden al publicar).
    /// Si pasa, se promueve con <see cref="AlmacenModeloLlamadas"/> y la API lo usa en la siguiente puntuación.</para>
    /// <para>NestoAPI#618: en la misma ejecución, después del general, el modelo por subgrupo (<c>modelo_llamadas_subgrupo.zip</c>,
    /// objetivo «pedido en 7 días que incluya el subgrupo»), con la misma puerta; mientras no hay ninguno, compara con lo que se
    /// hace hoy (el general con el subgrupo pisado). Su propio aviso.</para>
    /// <para>Aviso en la campana de Nesto (buzón + SignalR) a <see cref="CLAVE_USUARIOS_AVISO"/> (por defecto Carlos) en todos los
    /// casos; los errores, además, a ELMAH. Sin reintentos automáticos: un reintento caería en horario y carga la BD.</para>
    /// </summary>
    public class ReentrenamientoModeloLlamadasJobsService
    {
        public const string ID_JOB = "reentrenar-modelo-llamadas";
        /// <summary>Todos los sábados a las 02:30; el job sale si no es el primero del mes.</summary>
        public const string CRON = "30 2 * * 6";
        /// <summary>Cola propia, con su servidor de un hilo (Startup).</summary>
        public const string COLA = "entrenamiento";
        /// <summary>Si por lo que sea (reciclado del grupo de aplicaciones, cola atascada) el programado arranca a esta hora o más tarde, no entrena: sería en horario.</summary>
        internal const int HORA_LIMITE_PROGRAMADO = 7;
        internal const string CLAVE_MAX_BAJADA_PRECISION = "ModeloLlamadas:MaxBajadaPrecision";
        internal const string CLAVE_MAX_BAJADA_AUC = "ModeloLlamadas:MaxBajadaAuc";
        internal const string CLAVE_USUARIOS_AVISO = "ModeloLlamadas:UsuariosAviso";
        internal const string USUARIOS_AVISO_POR_DEFECTO = "Carlos";
        internal const string TITULO_AVISO = "Modelo de llamadas";
        /// <summary>NestoAPI#618: nombre del modelo por subgrupo en los avisos («Modelo de llamadas por subgrupo: promovido…»).</summary>
        internal const string NOMBRE_SUBGRUPO = "Modelo de llamadas por subgrupo";
        internal const string USO_GENERAL = "Ya lo usan las sugerencias de contacto.";
        internal const string ORIGEN_PROGRAMADO = "job mensual";

        internal const string COMO_REENTRENAR = "Lo hace el job «reentrenar-modelo-llamadas» de NestoAPI el primer sábado de cada mes a las 02:30 " +
            "(cola «entrenamiento» de Hangfire). A mano: `POST api/Rapports/ReentrenarModeloLlamadas?simular=true|false` (Dirección o " +
            "Informática); con simular=true entrena y evalúa pero no promueve. Umbrales: appSettings ModeloLlamadas:MaxBajadaPrecision " +
            "(puntos) y ModeloLlamadas:MaxBajadaAuc. Para volver al anterior: copiar modelo_llamadas_anterior.zip encima de modelo_llamadas.zip.";

        internal const string COMO_REENTRENAR_SUBGRUPO = "Lo entrena el mismo job «reentrenar-modelo-llamadas» justo después del general, " +
            "con la misma puerta (appSettings ModeloLlamadas:MaxBajadaPrecision y ModeloLlamadas:MaxBajadaAuc). Mientras no hay modelo por " +
            "subgrupo, se compara con lo que se hace hoy (el general con el subgrupo pisado). La API solo lo usa con el appSetting " +
            "ModeloLlamadas:Subgrupo:Activo = true y un subgrupo elegido. Para volver al anterior: copiar modelo_llamadas_subgrupo_anterior.zip " +
            "encima de modelo_llamadas_subgrupo.zip (o borrar modelo_llamadas_subgrupo.zip para volver al general con el subgrupo pisado).";

        private readonly IEntrenadorModeloLlamadas entrenador;
        private readonly AlmacenModeloLlamadas almacen;
        private readonly AlmacenModeloLlamadas almacenSubgrupo;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<DateTime> ahora;
        private readonly Func<string, string> leerParametro;
        private readonly Action asegurarNativo;
        private readonly Action<Exception> registrarError;

        public ReentrenamientoModeloLlamadasJobsService() : this(null)
        {
        }

        /// <param name="almacenSubgrupo">NestoAPI#618. Null = el de producción o, si se pasa <paramref name="almacen"/>, su misma carpeta.</param>
        internal ReentrenamientoModeloLlamadasJobsService(IEntrenadorModeloLlamadas entrenador, AlmacenModeloLlamadas almacen = null,
            IServicioNotificacionesPush notificaciones = null, Func<DateTime> ahora = null, Func<string, string> leerParametro = null,
            Action asegurarNativo = null, Action<Exception> registrarError = null, AlmacenModeloLlamadas almacenSubgrupo = null)
        {
            this.entrenador = entrenador ?? new EntrenadorModeloLlamadas();
            this.almacen = almacen ?? AlmacenModeloLlamadas.Produccion();
            this.almacenSubgrupo = almacenSubgrupo ?? (almacen == null
                ? AlmacenModeloLlamadas.ProduccionSubgrupo()
                : new AlmacenModeloLlamadas(almacen.Carpeta, null, AlmacenModeloLlamadas.BASE_SUBGRUPO));
            this.notificaciones = notificaciones ?? new ServicioNotificacionesPush();
            this.ahora = ahora ?? (() => DateTime.Now);
            this.leerParametro = leerParametro ?? (clave => ConfigurationManager.AppSettings[clave]);
            this.asegurarNativo = asegurarNativo ?? (() => _ = CargadorLightGbmNativo.Asegurar());
            this.registrarError = registrarError ?? ElmahHelper.Log;
        }

        /// <summary>Punto de entrada del job recurrente (todos los sábados; solo hace algo el primero del mes de madrugada).</summary>
        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public static void ReentrenarProgramado()
        {
            var servicio = new ReentrenamientoModeloLlamadasJobsService();
            if (!TocaReentrenar(servicio.ahora()))
            {
                return;
            }
            _ = servicio.Ejecutar(simular: false, ORIGEN_PROGRAMADO);
        }

        /// <summary>Lo que encola POST api/Rapports/ReentrenarModeloLlamadas.</summary>
        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public static void ReentrenarManual(bool simular, string usuario)
        {
            _ = new ReentrenamientoModeloLlamadasJobsService().Ejecutar(simular, string.IsNullOrWhiteSpace(usuario) ? "a mano" : $"a mano ({usuario})");
        }

        /// <summary>Primer sábado del mes (día 1 a 7) y de madrugada (antes de las <see cref="HORA_LIMITE_PROGRAMADO"/>).</summary>
        internal static bool TocaReentrenar(DateTime ahora)
            => ahora.DayOfWeek == DayOfWeek.Saturday && ahora.Day <= 7 && ahora.Hour < HORA_LIMITE_PROGRAMADO;

        /// <summary>
        /// Entrena, compara, promueve si pasa la puerta (y no es simulación) y avisa. Nunca lanza. Devuelve lo del modelo general.
        /// <para>NestoAPI#618: después, si el general no ha dado error (un error de BD o de la librería nativa se repetiría), lo
        /// mismo con el modelo por subgrupo, con su propio aviso. Se compara con el modelo general que estaba activo AL EMPEZAR
        /// (si el general se acaba de promover, el nuevo ya vio todos los contactos y la puerta no tendría con qué medir).</para>
        /// </summary>
        public EstadoReentrenamiento Ejecutar(bool simular, string origen)
        {
            string generalAlEmpezar = null;
            DateTime? corteGeneralAlEmpezar = null;
            try
            {
                generalAlEmpezar = almacen.RutaModeloActivo;
                corteGeneralAlEmpezar = almacen.CorteModeloActivo();
            }
            catch (Exception)
            {
                // Si no se puede leer, el general lo dirá en su propio intento.
            }

            EstadoReentrenamiento estado = EjecutarModelo(TITULO_AVISO, almacen, opciones => entrenador.Evaluar(opciones, ahora().Date),
                Opciones, USO_GENERAL, simular, origen, asegurarNativo);
            if (estado == EstadoReentrenamiento.Error)
            {
                return estado;
            }

            string general = estado == EstadoReentrenamiento.Promovido && string.Equals(generalAlEmpezar, almacen.RutaModelo, StringComparison.OrdinalIgnoreCase)
                ? almacen.RutaAnterior
                : generalAlEmpezar;
            _ = EjecutarSubgrupo(simular, origen, general, corteGeneralAlEmpezar);
            return estado;
        }

        /// <summary>NestoAPI#618: el modelo por subgrupo, contra el suyo o contra <paramref name="modeloGeneral"/> con el subgrupo pisado.</summary>
        internal EstadoReentrenamiento EjecutarSubgrupo(bool simular, string origen, string modeloGeneral, DateTime? corteGeneral)
            => EjecutarModelo(NOMBRE_SUBGRUPO, almacenSubgrupo, opciones => entrenador.EvaluarSubgrupo(opciones, ahora().Date),
                () => OpcionesSubgrupo(modeloGeneral, corteGeneral), UsoSubgrupo(), simular, origen, null);

        /// <summary>NestoAPI#618: lo que se dice al promover el de subgrupo, según el interruptor (en sombra la API no lo usa).</summary>
        internal string UsoSubgrupo() => ProbabilidadesContactoModelo.SubgrupoActivo(leerParametro)
            ? "Lo usan las sugerencias de contacto al elegir un subgrupo."
            : $"En sombra: la API no lo usa hasta poner {ProbabilidadesContactoModelo.CLAVE_SUBGRUPO_ACTIVO} = true en el Web.config.";

        private EstadoReentrenamiento EjecutarModelo(string nombre, AlmacenModeloLlamadas almacenModelo,
            Func<OpcionesReentrenamiento, IEvaluacionModeloLlamadas> evaluar, Func<OpcionesReentrenamiento> crearOpciones,
            string usoSiPromovido, bool simular, string origen, Action previo)
        {
            string temporal = null;
            try
            {
                previo?.Invoke();
                IEvaluacionModeloLlamadas evaluacion = evaluar(crearOpciones());
                DecisionPuerta decision = evaluacion.Decision;

                if (!decision.Promover || simular)
                {
                    string resultado = decision.Promover ? "pasa (simulación: no se promueve)" : (simular ? "no pasa (simulación)" : "no pasa");
                    almacenModelo.GuardarInforme(evaluacion.Readme(null, resultado));
                    EstadoReentrenamiento estado = decision.Promover ? EstadoReentrenamiento.Pasaria : EstadoReentrenamiento.NoPromovido;
                    Avisar(TextoAviso(nombre, estado, simular, decision.Motivo, almacenModelo.RutaUltimoInforme, usoSiPromovido));
                    return estado;
                }

                temporal = almacenModelo.NuevaRutaTemporal();
                evaluacion.EntrenarFinalYGuardar(temporal);
                string readme = evaluacion.Readme(almacenModelo.RutaModelo, "pasa: promovido");
                almacenModelo.Promover(temporal, evaluacion.Metadatos(ahora(), origen), readme);
                temporal = null;
                almacenModelo.GuardarInforme(readme);
                Avisar(TextoAviso(nombre, EstadoReentrenamiento.Promovido, false, evaluacion.Comparacion, almacenModelo.RutaModelo, usoSiPromovido));
                return EstadoReentrenamiento.Promovido;
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"[{nombre}] Error en el reentrenamiento ({origen}{(simular ? ", simulación" : "")}): {ex.Message}", ex));
                Avisar(TextoAviso(nombre, EstadoReentrenamiento.Error, simular, ex.Message, null, usoSiPromovido));
                return EstadoReentrenamiento.Error;
            }
            finally
            {
                almacenModelo.Descartar(temporal);
            }
        }

        /// <summary>Completo (3 años), solo LightGBM, contra el modelo activo y con los umbrales del Web.config o los del núcleo.</summary>
        internal OpcionesReentrenamiento Opciones() => new OpcionesReentrenamiento
        {
            Completo = true,
            EvaluarAlternativas = false,
            ModeloActual = almacen.RutaModeloActivo,
            CorteActual = almacen.CorteModeloActivo(),
            MaxBajadaPrecision = Numero(CLAVE_MAX_BAJADA_PRECISION, PuertaCalidad.MAX_BAJADA_PRECISION_POR_DEFECTO * 100) / 100,
            MaxBajadaAuc = Numero(CLAVE_MAX_BAJADA_AUC, PuertaCalidad.MAX_BAJADA_AUC_POR_DEFECTO)
        };

        /// <summary>
        /// NestoAPI#618: como <see cref="Opciones"/>, contra el modelo por subgrupo activo (con su corte) o, si aún no hay, contra
        /// el general con el subgrupo pisado (<paramref name="modeloGeneral"/>, con <paramref name="corteGeneral"/>).
        /// </summary>
        internal OpcionesReentrenamiento OpcionesSubgrupo(string modeloGeneral, DateTime? corteGeneral) => new OpcionesReentrenamiento
        {
            Completo = true,
            EvaluarAlternativas = false,
            ModeloActual = almacenSubgrupo.RutaModeloActivo,
            CorteActual = almacenSubgrupo.RutaModeloActivo != null ? almacenSubgrupo.CorteModeloActivo() : corteGeneral,
            ModeloGeneral = modeloGeneral,
            MaxBajadaPrecision = Numero(CLAVE_MAX_BAJADA_PRECISION, PuertaCalidad.MAX_BAJADA_PRECISION_POR_DEFECTO * 100) / 100,
            MaxBajadaAuc = Numero(CLAVE_MAX_BAJADA_AUC, PuertaCalidad.MAX_BAJADA_AUC_POR_DEFECTO)
        };

        private double Numero(string clave, double porDefecto)
        {
            string valor = leerParametro(clave);
            return !string.IsNullOrWhiteSpace(valor)
                && double.TryParse(valor.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double numero)
                && numero >= 0
                ? numero
                : porDefecto;
        }

        internal static string TextoAviso(EstadoReentrenamiento estado, bool simular, string detalle, string ruta)
            => TextoAviso(TITULO_AVISO, estado, simular, detalle, ruta, USO_GENERAL);

        /// <summary>NestoAPI#618: el del modelo por subgrupo dice «se queda el que había» en vez de «el de producción no se toca».</summary>
        internal static string TextoAviso(string nombre, EstadoReentrenamiento estado, bool simular, string detalle, string ruta, string usoSiPromovido)
        {
            bool general = nombre == TITULO_AVISO;
            switch (estado)
            {
                case EstadoReentrenamiento.Promovido:
                    return $"{nombre}: promovido ({detalle}). {usoSiPromovido} Modelo: {ruta}";
                case EstadoReentrenamiento.Pasaria:
                    return general
                        ? $"{nombre}: simulación, pasaría la puerta ({detalle}). No se ha tocado el de producción. Informe: {ruta}"
                        : $"{nombre}: simulación, pasaría la puerta ({detalle}). No se ha cambiado nada. Informe: {ruta}";
                case EstadoReentrenamiento.NoPromovido:
                    return general
                        ? $"{nombre}: {(simular ? "simulación, " : "")}no promovido ({detalle}). El de producción no se toca. Informe: {ruta}"
                        : $"{nombre}: {(simular ? "simulación, " : "")}no promovido ({detalle}). Se queda el que había. Informe: {ruta}";
                default:
                    return general
                        ? $"{nombre}: error{(simular ? " en la simulación" : "")} ({detalle}). El de producción no se toca. Detalle en ELMAH."
                        : $"{nombre}: error{(simular ? " en la simulación" : "")} ({detalle}). Se queda el que había. Detalle en ELMAH.";
            }
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

        /// <summary>A la campana de Nesto. Si falla el aviso, a ELMAH (el reentrenamiento ya está hecho o descartado).</summary>
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
                    registrarError(new Exception($"[{TITULO_AVISO}] No se pudo avisar a {usuario}: {ex.Message}. Aviso: {texto}", ex));
                }
            }
        }
    }
}
