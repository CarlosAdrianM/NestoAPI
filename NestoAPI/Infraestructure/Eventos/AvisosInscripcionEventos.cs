using Hangfire;
using NestoAPI.Models;
using System;
using System.Data.Entity;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>
    /// NestoAPI#875: lo que le toca a una inscripción en el momento en que cambia (se crea, pasa a online o queda pagada) y que
    /// normalmente le llegaría con un envío programado que ya ha pasado: el recordatorio con el enlace de la conexión online.
    /// En segundo plano (Hangfire): no frena la inscripción y, si falla, no la deshace. Idempotente (cada envío tiene su marca en
    /// la inscripción), así que encolarlo de más no duplica nada.
    /// </summary>
    public static class AvisosInscripcionEventosJobs
    {
        /// <summary>Encola los avisos de la inscripción. Nunca lanza.</summary>
        public static void Encolar(int inscripcionId)
        {
            try
            {
                _ = BackgroundJob.Enqueue(() => Procesar(inscripcionId));
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Eventos #875] No se han podido encolar los avisos de la inscripción {inscripcionId}: {ex.Message}", ex));
            }
        }

        [AutomaticRetry(Attempts = 2)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static async Task Procesar(int inscripcionId)
        {
            using (NVEntities db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                EventoInscripcion inscripcion = await db.EventosInscripciones.SingleOrDefaultAsync(i => i.Id == inscripcionId).ConfigureAwait(false);
                if (inscripcion == null)
                {
                    return;
                }
                Evento evento = await db.Eventos.SingleOrDefaultAsync(e => e.Id == inscripcion.EventoId).ConfigureAwait(false);
                if (evento == null)
                {
                    return;
                }
                string forma = (await RepositorioFormaAsistenciaEventos.LeerSinFallar(new RepositorioFormaAsistenciaEventos(db), new[] { inscripcion.Id })
                    .ConfigureAwait(false)).TryGetValue(inscripcion.Id, out string f) ? f : null;
                string enlace = await RepositorioEnlaceOnlineEventos.LeerUnoSinFallar(new RepositorioEnlaceOnlineEventos(db), evento.Id).ConfigureAwait(false);
                try
                {
                    _ = await EnlaceOnlineEventosJobs.AlInscribirse(EnlaceOnlineEventosJobs.Dependencias(db), evento, inscripcion, forma, enlace).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Eventos #875] No se ha podido mandar el enlace a la inscripción {inscripcionId}: {ex.Message}", ex));
                }
            }
        }
    }
}
