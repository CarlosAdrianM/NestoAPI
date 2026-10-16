using Hangfire;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Models.Novedades;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>
    /// Recordatorios de respuesta (Carlos, 16/10/26): recorre las sugerencias e incidencias que esperan respuesta
    /// del autor y, según <see cref="ReglasEsperaRespuestaNovedades.Decidir"/>, le recuerda la pregunta (comentario
    /// del asistente con su @mención y aviso a su campana o a su móvil) o la da por cerrada. En ensayo solo lista.
    /// </summary>
    public class RecordatoriosRespuestaNovedades
    {
        internal const string TIPO_NOTIFICACION = "NovedadComentario";

        private readonly IRepositorioEsperaRespuestaNovedades repositorio;
        private readonly IServicioFeedbackNovedades feedback;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<DateTime> reloj;
        private readonly Func<DateTime, bool> esLaborable;
        private readonly Func<bool> activo;

        public RecordatoriosRespuestaNovedades()
            : this(new RepositorioEsperaRespuestaNovedadesSql(), new ServicioFeedbackNovedades(), new ServicioNotificacionesPush(),
                  () => DateTime.Now, EsLaborableOficina, RecordatoriosRespuestaNovedadesJobsService.Activo)
        {
        }

        internal RecordatoriosRespuestaNovedades(IRepositorioEsperaRespuestaNovedades repositorio, IServicioFeedbackNovedades feedback,
            IServicioNotificacionesPush notificaciones, Func<DateTime> reloj, Func<DateTime, bool> esLaborable, Func<bool> activo)
        {
            this.repositorio = repositorio;
            this.feedback = feedback;
            this.notificaciones = notificaciones;
            this.reloj = reloj;
            this.esLaborable = esLaborable;
            this.activo = activo;
        }

        /// <summary>Festivos de la oficina (Algete), como el recordatorio de sugerencias de contacto y la remesa.</summary>
        internal static bool EsLaborableOficina(DateTime dia) => !GestorFestivos.EsFestivo(dia, Constantes.Almacenes.ALGETE);

        /// <summary>
        /// Con <paramref name="ensayo"/> solo dice lo que haría (aunque el interruptor esté apagado). Sin ensayo y con
        /// el interruptor apagado, no hace nada.
        /// </summary>
        public async Task<List<ResultadoRecordatorioRespuestaDTO>> Ejecutar(bool ensayo)
        {
            var resultados = new List<ResultadoRecordatorioRespuestaDTO>();
            if (!ensayo && (activo == null || !activo()))
            {
                return resultados;
            }
            DateTime ahora = reloj();
            foreach (EsperaRespuestaNovedad espera in repositorio.LeerEsperando() ?? new List<EsperaRespuestaNovedad>())
            {
                AccionEsperaRespuesta accion = ReglasEsperaRespuestaNovedades.Decidir(espera, ahora, esLaborable);
                if (accion == AccionEsperaRespuesta.Nada)
                {
                    continue;
                }
                bool esIncidencia = ReglasEsperaRespuestaNovedades.EsIncidencia(espera);
                int numero = accion == AccionEsperaRespuesta.Recordatorio ? espera.RecordatoriosRespuesta + 1 : 0;
                var resultado = new ResultadoRecordatorioRespuestaDTO
                {
                    NovedadId = espera.Id,
                    Titulo = espera.Titulo?.Trim(),
                    Autor = espera.SugeridaNombre?.Trim(),
                    EsperandoDesde = espera.EsperandoRespuestaDesde,
                    Accion = accion == AccionEsperaRespuesta.Recordatorio ? ReglasEsperaRespuestaNovedades.ACCION_RECORDATORIO : ReglasEsperaRespuestaNovedades.ACCION_CERRAR,
                    Recordatorio = numero,
                    Texto = accion == AccionEsperaRespuesta.Recordatorio
                        ? ReglasEsperaRespuestaNovedades.TextoRecordatorio(espera.SugeridaNombre, numero, esIncidencia)
                        : ReglasEsperaRespuestaNovedades.TextoCierre(espera.SugeridaNombre, esIncidencia),
                    Ensayo = ensayo
                };
                resultados.Add(resultado);
                if (ensayo)
                {
                    continue;
                }
                try
                {
                    int comentarioId = feedback.CrearComentario(new ComentarioNovedadAGrabar
                    {
                        NovedadId = espera.Id,
                        Usuario = ReglasFeedbackNovedades.USUARIO_ASISTENTE,
                        NombreVisible = ReglasFeedbackNovedades.NOMBRE_ASISTENTE,
                        Cliente = ReglasFeedbackNovedades.CLIENTE_ASISTENTE,
                        Texto = resultado.Texto
                    });
                    // Es automático: no hace falta que nadie lo revise en el feedback del día.
                    _ = feedback.MarcarRevisado(comentarioId);
                    if (accion == AccionEsperaRespuesta.Recordatorio)
                    {
                        repositorio.GuardarRecordatorio(espera.Id, (byte)numero, ahora);
                    }
                    else
                    {
                        repositorio.Cerrar(espera.Id, ahora);
                    }
                    await AvisarAlAutor(espera, comentarioId, resultado.Texto).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    resultado.Error = ex.Message;
                    Registrar(new Exception($"Novedades (recordatorios de respuesta): no se pudo tratar la {espera.Id}. {ex.Message}", ex));
                }
            }
            return resultados;
        }

        /// <summary>
        /// El aviso le llega al autor por el programa de la sugerencia, como cuando el asistente le contesta: Nesto,
        /// a su campana; NestoApp, push; Ariadna, a su campana. Nunca rompe el recordatorio.
        /// </summary>
        private async Task AvisarAlAutor(EsperaRespuestaNovedad espera, int comentarioId, string texto)
        {
            if (notificaciones == null)
            {
                return;
            }
            try
            {
                var notificacion = new NotificacionPushDTO
                {
                    Titulo = ReglasFeedbackNovedades.NOMBRE_ASISTENTE + " le ha mencionado en Novedades",
                    Cuerpo = texto.Length > 200 ? texto.Substring(0, 197) + "…" : texto,
                    Tipo = TIPO_NOTIFICACION,
                    Datos = new Dictionary<string, string>
                    {
                        ["tipo"] = TIPO_NOTIFICACION,
                        ["novedadId"] = espera.Id.ToString(),
                        ["comentarioId"] = comentarioId.ToString()
                    }
                };
                string ambito = espera.Ambito?.Trim();
                if (string.Equals(ambito, Constantes.Aplicaciones.NESTO_APP, StringComparison.OrdinalIgnoreCase))
                {
                    // En la app los dispositivos van por el UserName, que es el nombre visible
                    _ = await notificaciones.EnviarAUsuario(espera.SugeridaNombre?.Trim(), Constantes.Aplicaciones.NESTO_APP, notificacion).ConfigureAwait(false);
                }
                else if (string.Equals(ambito, Constantes.Aplicaciones.ARIADNA, StringComparison.OrdinalIgnoreCase))
                {
                    await notificaciones.GuardarEnBuzonDeUsuario(espera.SugeridaNombre?.Trim(), Constantes.Aplicaciones.ARIADNA, notificacion).ConfigureAwait(false);
                }
                else
                {
                    await notificaciones.GuardarEnBuzonDeUsuario(espera.SugeridaPor?.Trim(), Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Registrar(new Exception($"Novedades (recordatorios de respuesta): no se pudo avisar al autor de la {espera.Id}. {ex.Message}", ex));
            }
        }

        private static void Registrar(Exception ex)
        {
            try
            {
                ElmahHelper.Log(ex, "Sistema (recordatorios de Novedades)");
            }
            catch
            {
                // El diagnóstico nunca rompe el job.
            }
        }
    }

    /// <summary>Recordatorios de respuesta (Carlos, 16/10/26): el job de Hangfire de lunes a viernes a las 9:00.</summary>
    public static class RecordatoriosRespuestaNovedadesJobsService
    {
        public const string ID_JOB = "novedades-recordatorios-respuesta";
        public const string CRON = "0 9 * * 1-5";

        /// <summary>Web.config <c>Novedades:RecordatoriosRespuesta</c> = true. Sin la clave, apagado.</summary>
        public static bool Activo() =>
            string.Equals(ConfigurationManager.AppSettings[ReglasEsperaRespuestaNovedades.CLAVE_INTERRUPTOR]?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static async Task Procesar()
        {
            try
            {
                if (!Activo() || !RecordatoriosRespuestaNovedades.EsLaborableOficina(DateTime.Today))
                {
                    return;
                }
                _ = await new RecordatoriosRespuestaNovedades().Ejecutar(ensayo: false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("Novedades (recordatorios de respuesta): error en el job. " + ex.Message, ex), "Sistema (recordatorios de Novedades)");
                throw;
            }
        }
    }
}
