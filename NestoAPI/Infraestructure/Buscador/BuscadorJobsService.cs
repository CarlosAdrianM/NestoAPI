using Hangfire;
using System;

namespace NestoAPI.Infraestructure.Buscador
{
    /// <summary>
    /// Reindexado diario del buscador Lucene (NestoAPI#402: lo programado vive en Hangfire, no en
    /// el Task Scheduler). Va a las 20:30 porque <c>prdActualizarClasificacionProductos</c>, que
    /// rellena ClasificacionMasVendidos, corre a las 20:00 y tarda unos cinco minutos; el índice
    /// guarda esa posición para ponderar los resultados, así que tiene que regenerarse después.
    /// </summary>
    public class BuscadorJobsService
    {
        // Sin reintentos: si falla, el índice de ayer sigue sirviendo y mañana vuelve a intentarlo.
        // Y nunca dos a la vez: el IndexWriter bloquea el directorio y el segundo reventaría.
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static void Reindexar()
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Reindexando el buscador...");
            LuceneBuscador.IndexarTodo();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Buscador reindexado");
        }
    }
}
