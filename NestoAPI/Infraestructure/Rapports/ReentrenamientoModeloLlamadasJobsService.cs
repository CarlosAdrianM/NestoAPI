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

    /// <summary>El de verdad: <see cref="RepositorioDatosSql"/> con NestoConnection y <see cref="ProcesoReentrenamiento"/>.</summary>
    public class EntrenadorModeloLlamadas : IEntrenadorModeloLlamadas
    {
        public IEvaluacionModeloLlamadas Evaluar(OpcionesReentrenamiento opciones, DateTime hoy)
        {
            string cadena = ConfigurationManager.ConnectionStrings["NestoConnection"].ConnectionString;
            (DateTime desde, DateTime hasta, _) = VentanaReentrenamiento.Calcular(hoy, opciones.Completo, opciones.Meses, opciones.MesesValidacion);
            DatosCrudos datos = new RepositorioDatosSql(() => new SqlConnection(cadena))
                .Leer(desde, hasta, opciones.Completo ? null : opciones.Vendedor, CalculadoraFeatures.MESES_HISTORIAL, CalculadoraFeatures.VENTANA_POSITIVO_DIAS);
            return new Evaluacion(ProcesoReentrenamiento.Evaluar(datos, opciones, hoy));
        }

        private sealed class Evaluacion : IEvaluacionModeloLlamadas
        {
            private readonly ProcesoReentrenamiento proceso;

            public Evaluacion(ProcesoReentrenamiento proceso)
            {
                this.proceso = proceso;
            }

            public bool DatosSuficientes => proceso.DatosSuficientes;
            public DecisionPuerta Decision => proceso.Decision;
            public string Comparacion => proceso.Comparacion;
            public void EntrenarFinalYGuardar(string ruta) => _ = proceso.EntrenarFinalYGuardar(ruta);
            public string Readme(string rutaModelo, string resultado) => proceso.Readme(rutaModelo, resultado, ReentrenamientoModeloLlamadasJobsService.COMO_REENTRENAR);
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
        internal const string ORIGEN_PROGRAMADO = "job mensual";

        internal const string COMO_REENTRENAR = "Lo hace el job «reentrenar-modelo-llamadas» de NestoAPI el primer sábado de cada mes a las 02:30 " +
            "(cola «entrenamiento» de Hangfire). A mano: `POST api/Rapports/ReentrenarModeloLlamadas?simular=true|false` (Dirección o " +
            "Informática); con simular=true entrena y evalúa pero no promueve. Umbrales: appSettings ModeloLlamadas:MaxBajadaPrecision " +
            "(puntos) y ModeloLlamadas:MaxBajadaAuc. Para volver al anterior: copiar modelo_llamadas_anterior.zip encima de modelo_llamadas.zip.";

        private readonly IEntrenadorModeloLlamadas entrenador;
        private readonly AlmacenModeloLlamadas almacen;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<DateTime> ahora;
        private readonly Func<string, string> leerParametro;
        private readonly Action asegurarNativo;
        private readonly Action<Exception> registrarError;

        public ReentrenamientoModeloLlamadasJobsService() : this(null)
        {
        }

        internal ReentrenamientoModeloLlamadasJobsService(IEntrenadorModeloLlamadas entrenador, AlmacenModeloLlamadas almacen = null,
            IServicioNotificacionesPush notificaciones = null, Func<DateTime> ahora = null, Func<string, string> leerParametro = null,
            Action asegurarNativo = null, Action<Exception> registrarError = null)
        {
            this.entrenador = entrenador ?? new EntrenadorModeloLlamadas();
            this.almacen = almacen ?? AlmacenModeloLlamadas.Produccion();
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

        /// <summary>Entrena, compara, promueve si pasa la puerta (y no es simulación) y avisa. Nunca lanza.</summary>
        public EstadoReentrenamiento Ejecutar(bool simular, string origen)
        {
            string temporal = null;
            try
            {
                asegurarNativo();
                DateTime inicio = ahora();
                OpcionesReentrenamiento opciones = Opciones();
                IEvaluacionModeloLlamadas evaluacion = entrenador.Evaluar(opciones, inicio.Date);
                DecisionPuerta decision = evaluacion.Decision;

                if (!decision.Promover || simular)
                {
                    string resultado = decision.Promover ? "pasa (simulación: no se promueve)" : (simular ? "no pasa (simulación)" : "no pasa");
                    almacen.GuardarInforme(evaluacion.Readme(null, resultado));
                    EstadoReentrenamiento estado = decision.Promover ? EstadoReentrenamiento.Pasaria : EstadoReentrenamiento.NoPromovido;
                    Avisar(TextoAviso(estado, simular, decision.Motivo, almacen.RutaUltimoInforme));
                    return estado;
                }

                temporal = almacen.NuevaRutaTemporal();
                evaluacion.EntrenarFinalYGuardar(temporal);
                string readme = evaluacion.Readme(almacen.RutaModelo, "pasa: promovido");
                almacen.Promover(temporal, evaluacion.Metadatos(ahora(), origen), readme);
                temporal = null;
                almacen.GuardarInforme(readme);
                Avisar(TextoAviso(EstadoReentrenamiento.Promovido, false, evaluacion.Comparacion, almacen.RutaModelo));
                return EstadoReentrenamiento.Promovido;
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"[Modelo de llamadas] Error en el reentrenamiento ({origen}{(simular ? ", simulación" : "")}): {ex.Message}", ex));
                Avisar(TextoAviso(EstadoReentrenamiento.Error, simular, ex.Message, null));
                return EstadoReentrenamiento.Error;
            }
            finally
            {
                almacen.Descartar(temporal);
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
        {
            switch (estado)
            {
                case EstadoReentrenamiento.Promovido:
                    return $"Modelo de llamadas: promovido ({detalle}). Ya lo usan las sugerencias de contacto. Modelo: {ruta}";
                case EstadoReentrenamiento.Pasaria:
                    return $"Modelo de llamadas: simulación, pasaría la puerta ({detalle}). No se ha tocado el de producción. Informe: {ruta}";
                case EstadoReentrenamiento.NoPromovido:
                    return $"Modelo de llamadas: {(simular ? "simulación, " : "")}no promovido ({detalle}). El de producción no se toca. Informe: {ruta}";
                default:
                    return $"Modelo de llamadas: error{(simular ? " en la simulación" : "")} ({detalle}). El de producción no se toca. Detalle en ELMAH.";
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
                    registrarError(new Exception($"[Modelo de llamadas] No se pudo avisar a {usuario}: {ex.Message}. Aviso: {texto}", ex));
                }
            }
        }
    }
}
