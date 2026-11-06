using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.VigilanciaHangfire
{
    /// <summary>NestoAPI#820: un job de Hangfire en estado Processing (leído de HangFire.Job + HangFire.State + HangFire.JobQueue).</summary>
    public class JobEnCurso
    {
        public long Id { get; set; }
        /// <summary>«Clase.Método» sacado de InvocationData.</summary>
        public string Nombre { get; set; }
        /// <summary>La cola de la que lo cogió el worker (null si ya no tiene fila en JobQueue).</summary>
        public string Cola { get; set; }
        /// <summary>Cuándo pasó a Processing, en UTC (como lo guarda Hangfire).</summary>
        public DateTime EmpezoUtc { get; set; }
    }

    /// <summary>NestoAPI#820: lo que hay que avisar. <see cref="Clave"/> identifica el episodio para no repetirlo.</summary>
    public class AvisoHangfire
    {
        public string Clave { get; set; }
        public string Titulo { get; set; }
        public string Texto { get; set; }
    }

    /// <summary>
    /// NestoAPI#820 (incidente 04-05/11/26): la regla del vigilante, pura. El 04/11 a las 19:55 el único worker de «default» se
    /// quedó en SincronizarProductos y hasta el reciclado de las 09:27 se acumularon 635 jobs sin ejecutar, sin nada en ELMAH.
    /// Se avisa de dos cosas: un job en marcha más de lo normal y muchos jobs esperando a que alguien los coja.
    /// </summary>
    public static class VigilanteHangfire
    {
        /// <summary>Un job de las colas normales tarda segundos o pocos minutos: 15 minutos ya es que está colgado.</summary>
        internal static readonly TimeSpan MAXIMO_EN_MARCHA = TimeSpan.FromMinutes(15);

        /// <summary>
        /// Las colas de los jobs largos (reentrenar modelos, indicadores y propuestas de stock) tardan horas por diseño: con 15
        /// minutos avisarían cada mes de algo normal. Tienen su propio servidor, así que no paran a nadie más.
        /// </summary>
        internal static readonly TimeSpan MAXIMO_EN_MARCHA_COLAS_LARGAS = TimeSpan.FromHours(8);

        internal static readonly string[] COLAS_LARGAS =
        {
            Rapports.ReentrenamientoModeloLlamadasJobsService.COLA,
            IndicadoresStock.IndicadoresStockJobsService.COLA
        };

        /// <summary>
        /// NestoAPI#835: los jobs que son largos por naturaleza, por su nombre («Clase.Método», el de <see cref="JobEnCurso.Nombre"/>).
        /// Las dos primeras noches del vigilante avisaron de dos que terminaron bien: ClasificacionRapportsJobsService.Procesar
        /// (05/11 22:15, 22 min) y SincronizarProductos (06/11 01:05, la pasada nocturna con todo el stock encolado, 15 min).
        /// El resto, <see cref="MAXIMO_EN_MARCHA"/>.
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, TimeSpan> MAXIMO_EN_MARCHA_POR_JOB =
            new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(Rapports.ClasificacionRapportsJobsService) + "." + nameof(Rapports.ClasificacionRapportsJobsService.Procesar)] = TimeSpan.FromMinutes(45),
                [nameof(SincronizacionJobsService) + "." + nameof(SincronizacionJobsService.SincronizarProductos)] = TimeSpan.FromMinutes(45)
            };

        /// <summary>
        /// NestoAPI#835: por cola, para los jobs sin umbral propio. La pasada nocturna de SincronizarProductos y la de cada 5 minutos
        /// son el mismo job recurrente (mismo método), así que no se distinguen por el nombre: la cola «sincronizacion» (que tiene
        /// su propio servidor) también tiene 45 minutos.
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, TimeSpan> MAXIMO_EN_MARCHA_POR_COLA =
            new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
            {
                [SincronizacionJobsService.COLA] = TimeSpan.FromMinutes(45)
            };

        /// <summary>En un día normal la cola está a 0-10; el 05/11 llegó a 635.</summary>
        internal const int MAXIMO_ENCOLADOS = 100;

        internal const string CLAVE_COLA_GRANDE = "cola-grande";

        internal static string ClaveColgado(long id) => "colgado:" + id.ToString(CultureInfo.InvariantCulture);

        /// <param name="enCurso">Los jobs en Processing.</param>
        /// <param name="encolados">Los que esperan en HangFire.JobQueue sin que ningún worker los haya cogido.</param>
        /// <param name="ahoraUtc">La hora en UTC (Hangfire guarda en UTC).</param>
        public static List<AvisoHangfire> Evaluar(List<JobEnCurso> enCurso, int encolados, DateTime ahoraUtc)
        {
            var avisos = new List<AvisoHangfire>();
            foreach (JobEnCurso job in (enCurso ?? new List<JobEnCurso>()).Where(j => j != null).OrderBy(j => j.EmpezoUtc))
            {
                TimeSpan enMarcha = ahoraUtc - job.EmpezoUtc;
                if (enMarcha <= MaximoEnMarcha(job.Nombre, job.Cola))
                {
                    continue;
                }
                string cola = string.IsNullOrWhiteSpace(job.Cola) ? "desconocida" : job.Cola.Trim();
                avisos.Add(new AvisoHangfire
                {
                    Clave = ClaveColgado(job.Id),
                    Titulo = "Hangfire: job colgado",
                    Texto = $"El job {job.Nombre ?? "(sin nombre)"} (n.º {job.Id.ToString(CultureInfo.InvariantCulture)}, cola «{cola}») " +
                        $"lleva {TextoDuracion(enMarcha)} en marcha. Mientras siga así, los demás jobs de su cola no se ejecutan. " +
                        "Compruébelo en /hangfire y, si no avanza, recicle el pool de NestoAPI."
                });
            }
            if (encolados > MAXIMO_ENCOLADOS)
            {
                avisos.Add(new AvisoHangfire
                {
                    Clave = CLAVE_COLA_GRANDE,
                    Titulo = "Hangfire: cola atascada",
                    Texto = $"Hay {encolados.ToString(CultureInfo.InvariantCulture)} jobs de Hangfire esperando a ejecutarse " +
                        $"(lo normal es menos de {MAXIMO_ENCOLADOS.ToString(CultureInfo.InvariantCulture)}). Puede que un job colgado " +
                        "tenga parada la cola: compruébelo en /hangfire."
                });
            }
            return avisos;
        }

        /// <summary>
        /// NestoAPI#835: el umbral del job: el suyo si lo tiene (<see cref="MAXIMO_EN_MARCHA_POR_JOB"/>); si no, el de su cola
        /// (las largas, 8 horas; <see cref="MAXIMO_EN_MARCHA_POR_COLA"/>); si no, <see cref="MAXIMO_EN_MARCHA"/> (15 minutos).
        /// </summary>
        internal static TimeSpan MaximoEnMarcha(string nombre, string cola)
        {
            if (!string.IsNullOrWhiteSpace(nombre) && MAXIMO_EN_MARCHA_POR_JOB.TryGetValue(nombre.Trim(), out TimeSpan delJob))
            {
                return delJob;
            }
            return MaximoEnMarcha(cola);
        }

        internal static TimeSpan MaximoEnMarcha(string cola)
        {
            if (cola == null)
            {
                return MAXIMO_EN_MARCHA;
            }
            if (COLAS_LARGAS.Contains(cola.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                return MAXIMO_EN_MARCHA_COLAS_LARGAS;
            }
            return MAXIMO_EN_MARCHA_POR_COLA.TryGetValue(cola.Trim(), out TimeSpan deLaCola) ? deLaCola : MAXIMO_EN_MARCHA;
        }

        internal static string TextoDuracion(TimeSpan duracion)
        {
            int minutos = (int)Math.Floor(duracion.TotalMinutes);
            if (minutos < 60)
            {
                return minutos == 1 ? "1 minuto" : $"{minutos} minutos";
            }
            int horas = minutos / 60;
            int resto = minutos % 60;
            string textoHoras = horas == 1 ? "1 hora" : $"{horas} horas";
            return resto == 0 ? textoHoras : $"{textoHoras} y {(resto == 1 ? "1 minuto" : resto + " minutos")}";
        }
    }

    /// <summary>
    /// NestoAPI#820: un aviso UNA vez por episodio (como <see cref="Verifactu.DeduplicadorErroresVerifactu"/>, en memoria). Un
    /// episodio dura mientras la clave sigue saliendo en cada pasada; cuando deja de salir (el job acaba, la cola baja) se olvida y
    /// la próxima vez vuelve a avisar. Un reciclado del pool lo resetea: como mucho, un aviso repetido.
    /// </summary>
    internal static class DeduplicadorAvisosHangfire
    {
        private static readonly ConcurrentDictionary<string, bool> activos = new ConcurrentDictionary<string, bool>();

        /// <summary>De los avisos de esta pasada, los que no se habían avisado ya; olvida los episodios que han terminado.</summary>
        internal static List<AvisoHangfire> Nuevos(List<AvisoHangfire> actuales)
        {
            List<AvisoHangfire> lista = (actuales ?? new List<AvisoHangfire>()).Where(a => a != null).ToList();
            var claves = new HashSet<string>(lista.Select(a => a.Clave));
            foreach (string vieja in activos.Keys.Where(k => !claves.Contains(k)).ToList())
            {
                _ = activos.TryRemove(vieja, out _);
            }
            return lista.Where(a => activos.TryAdd(a.Clave, true)).ToList();
        }

        /// <summary>Solo para tests.</summary>
        internal static void Reset()
        {
            activos.Clear();
        }
    }
}
