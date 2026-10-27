using Hangfire;
using ModeloSugerenciasProductos.Aceptacion;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Text;

namespace NestoAPI.Infraestructure.SugerenciasProductos
{
    public enum EstadoEntrenamientoAceptacion
    {
        /// <summary>Entrenado y guardado; pasa la puerta (solo se usa para ordenar con ModeloSugerencias:RankingAceptacion = Activo).</summary>
        Pasa,
        /// <summary>Entrenado y guardado; no pasa la puerta (no se usa ni en la sombra).</summary>
        NoPasa,
        /// <summary>Por debajo del umbral de respuestas: ni se entrena (solo el informe).</summary>
        DatosInsuficientes,
        EnHorario,
        Error
    }

    /// <summary>
    /// NestoAPI#671 (corte 2e): reentrenamiento SEMANAL del modelo 2 de sugerencias (aceptación), con las respuestas ✓/✗ de los
    /// vendedores. Job «entrenar-modelo-aceptacion-sugerencias»: los domingos a las 03:30 (fuera de horario; el modelo 1 es el
    /// segundo sábado a las 02:30 y el de llamadas el primero), en la cola «entrenamiento» (servidor de un hilo: nunca coincide
    /// con ellos). Lee SugerenciasProductosMostradas (miles de filas, no LinPedidoVta), así que tarda poco, pero sigue la regla:
    /// de lunes a viernes de 7:00 a 21:00 no entrena.
    /// <para>Con menos de <see cref="OpcionesAceptacion.MinimoRespondidas"/> respuestas, <see cref="OpcionesAceptacion.MinimoAnadidos"/>
    /// «Añadido» o <see cref="OpcionesAceptacion.MinimoVendedores"/> vendedores no entrena: deja el informe (con las tasas por
    /// motivo y vendedor, que ya sirven) y no avisa (sería un aviso igual cada semana). Si entrena, guarda zip, json e informe con
    /// la fecha en la carpeta de modelos y avisa en la campana con la puerta. Pasar la puerta NO cambia lo que ven los vendedores
    /// salvo con <see cref="RankingAceptacionSugerencias.CLAVE_MODO"/> = Activo.</para>
    /// </summary>
    public class ReentrenamientoModeloAceptacionJobsService
    {
        public const string ID_JOB = "entrenar-modelo-aceptacion-sugerencias";
        /// <summary>Domingos a las 03:30.</summary>
        public const string CRON = "30 3 * * 0";
        public const string COLA = ReentrenamientoModeloLlamadasJobsService.COLA;
        internal const string TITULO_AVISO = "Modelo de aceptación de sugerencias";

        private readonly Func<List<MostradaAceptacion>> leer;
        private readonly AlmacenModeloSugerencias almacen;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<DateTime> ahora;
        private readonly Func<string, string> leerParametro;
        private readonly Action asegurarNativo;
        private readonly Action<Exception> registrarError;
        private readonly OpcionesAceptacion opciones;

        public ReentrenamientoModeloAceptacionJobsService() : this(null)
        {
        }

        internal ReentrenamientoModeloAceptacionJobsService(Func<List<MostradaAceptacion>> leer, AlmacenModeloSugerencias almacen = null,
            IServicioNotificacionesPush notificaciones = null, Func<DateTime> ahora = null, Func<string, string> leerParametro = null,
            Action asegurarNativo = null, Action<Exception> registrarError = null, OpcionesAceptacion opciones = null)
        {
            this.leer = leer ?? (() => SugerenciasAceptacionSql.LeerEntrenamiento(ConfigurationManager.ConnectionStrings["NestoConnection"].ConnectionString));
            this.almacen = almacen ?? AlmacenModeloSugerencias.Produccion();
            this.notificaciones = notificaciones ?? new ServicioNotificacionesPush();
            this.ahora = ahora ?? (() => DateTime.Now);
            this.leerParametro = leerParametro ?? (clave => ConfigurationManager.AppSettings[clave]);
            this.asegurarNativo = asegurarNativo ?? (() => _ = CargadorLightGbmNativo.Asegurar());
            this.registrarError = registrarError ?? ElmahHelper.Log;
            this.opciones = opciones ?? new OpcionesAceptacion();
        }

        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public static void EntrenarProgramado() => _ = new ReentrenamientoModeloAceptacionJobsService().Ejecutar(forzar: false, "job semanal");

        /// <summary>Entrena, mide frente a P1 y al bayesiano, guarda y avisa. Nunca lanza.</summary>
        public EstadoEntrenamientoAceptacion Ejecutar(bool forzar, string origen)
        {
            DateTime inicio = ahora();
            if (!forzar && ReentrenamientoModeloSugerenciasJobsService.EnHorario(inicio))
            {
                System.Diagnostics.Trace.TraceWarning($"[Modelo de aceptación] No se entrena ({origen}): son las {inicio:HH:mm} de un día laborable.");
                return EstadoEntrenamientoAceptacion.EnHorario;
            }

            string sufijo = AlmacenModeloSugerencias.Sufijo(inicio);
            var log = new StringBuilder();
            void Apuntar(string linea) => log.AppendLine($"{ahora():HH:mm:ss} {linea}");
            Apuntar($"Entrenamiento del modelo de aceptación de sugerencias (NestoAPI#671, corte 2e), {origen}{(forzar ? ", forzado" : "")}.");
            try
            {
                List<MostradaAceptacion> mostradas = leer() ?? new List<MostradaAceptacion>();
                Apuntar($"Leídas {mostradas.Count} filas (respondidas y señales débiles).");
                UmbralDatosAceptacion.Resultado umbral = UmbralDatosAceptacion.Comprobar(mostradas, opciones);
                if (umbral.Suficientes)
                {
                    asegurarNativo(); // entrenar LightGBM necesita la DLL nativa; puntuar, no
                }
                ProcesoAceptacion proceso = ProcesoAceptacion.Evaluar(mostradas, opciones, Apuntar);
                string rutaInforme = almacen.RutaInformeAceptacion(sufijo);
                almacen.Escribir(rutaInforme, proceso.Informe());
                if (!proceso.DatosSuficientes)
                {
                    Apuntar($"No se entrena: {proceso.Umbral.Motivo}.");
                    return EstadoEntrenamientoAceptacion.DatosInsuficientes;
                }

                string rutaModelo = almacen.RutaModeloAceptacion(sufijo);
                proceso.EntrenarFinalYGuardar(rutaModelo);
                almacen.Escribir(almacen.RutaMetadatosAceptacion(sufijo),
                    JsonConvert.SerializeObject(proceso.Metadatos(ahora(), origen, rutaModelo), Formatting.Indented), bom: false);
                EstadoEntrenamientoAceptacion estado = proceso.Decision.Pasa ? EstadoEntrenamientoAceptacion.Pasa : EstadoEntrenamientoAceptacion.NoPasa;
                Apuntar($"Terminado: {(proceso.Decision.Pasa ? "pasa" : "no pasa")} la puerta.");
                Avisar(TextoAviso(estado, proceso.Decision.Motivo, rutaInforme, RankingAceptacionSugerencias.LeerModo(leerParametro)));
                return estado;
            }
            catch (Exception ex)
            {
                Apuntar($"ERROR: {ex}");
                registrarError(new Exception($"[Modelo de aceptación] Error en el entrenamiento ({origen}): {ex.Message}", ex));
                Avisar(TextoAviso(EstadoEntrenamientoAceptacion.Error, ex.Message, null, ModoRankingAceptacion.Sombra));
                return EstadoEntrenamientoAceptacion.Error;
            }
            finally
            {
                try
                {
                    almacen.Escribir(almacen.RutaLogAceptacion(sufijo), log.ToString());
                }
                catch (Exception)
                {
                    // El log es una ayuda.
                }
            }
        }

        internal static string TextoAviso(EstadoEntrenamientoAceptacion estado, string motivo, string rutaInforme, ModoRankingAceptacion modo)
        {
            string uso = modo == ModoRankingAceptacion.Activo
                ? "Con RankingAceptacion = Activo, las sugerencias ya lo usan para ordenar."
                : $"Sigue en {modo.ToString().ToLower(CultureInfo.InvariantCulture)}: para que ordene, ModeloSugerencias:RankingAceptacion = Activo en Web.config.";
            switch (estado)
            {
                case EstadoEntrenamientoAceptacion.Pasa:
                    return $"Modelo de aceptación de sugerencias: entrenado, PASA la puerta ({motivo}). {uso} Informe: {rutaInforme}";
                case EstadoEntrenamientoAceptacion.NoPasa:
                    return $"Modelo de aceptación de sugerencias: entrenado, NO pasa la puerta ({motivo}). No se usa (la sombra sigue con el último que la pasó o con el bayesiano). Informe: {rutaInforme}";
                default:
                    return $"Modelo de aceptación de sugerencias: error en el entrenamiento ({motivo}). Nada cambia. Detalle en ELMAH.";
            }
        }

        private void Avisar(string texto)
        {
            var notificacion = new NotificacionPushDTO
            {
                Titulo = TITULO_AVISO,
                Cuerpo = texto,
                Tipo = NotificacionesController.TIPO_AVISO_NESTO,
                Datos = new Dictionary<string, string> { ["tipo"] = NotificacionesController.TIPO_AVISO_NESTO }
            };
            // Los mismos destinatarios que el modelo 1 (ModeloSugerencias:UsuariosAviso, por defecto Carlos)
            foreach (string usuario in ReentrenamientoModeloSugerenciasJobsService.Destinatarios(leerParametro))
            {
                try
                {
                    notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    registrarError(new Exception($"[Modelo de aceptación] No se pudo avisar a {usuario}: {ex.Message}. Aviso: {texto}", ex));
                }
            }
        }
    }
}
