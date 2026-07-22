using System.Collections.Concurrent;

namespace NestoAPI.Infraestructure.Verifactu
{
    /// <summary>
    /// NestoAPI#346: corta el ruido de los reintentos de Verifactu. Una factura atascada se
    /// reintenta en cada pasada del job y, sin esto, cada intento repite el MISMO error en ELMAH
    /// y dispara otro correo a administración (~12 al día por factura). Se recuerda el último
    /// error por clave y solo se considera "novedad" cuando el texto cambia. El estado es en
    /// memoria: un reciclaje del pool lo resetea y el primer intento posterior vuelve a avisar
    /// (aceptable: como mucho un aviso extra al día).
    /// </summary>
    internal static class DeduplicadorErroresVerifactu
    {
        private static readonly ConcurrentDictionary<string, string> ultimoErrorPorClave =
            new ConcurrentDictionary<string, string>();

        /// <summary>
        /// Registra el error y devuelve true si es una novedad para esa clave (primer error o
        /// texto distinto del último registrado). False = repetición exacta, no avisar otra vez.
        /// </summary>
        internal static bool EsNovedad(string clave, string error)
        {
            string texto = error ?? string.Empty;
            bool repetido = ultimoErrorPorClave.TryGetValue(clave, out string anterior) && anterior == texto;
            ultimoErrorPorClave[clave] = texto;
            return !repetido;
        }

        /// <summary>Limpia la clave cuando el envío por fin funciona (o deja de proceder).</summary>
        internal static void Limpiar(string clave)
        {
            _ = ultimoErrorPorClave.TryRemove(clave, out _);
        }

        /// <summary>Solo para tests: estado limpio entre tests.</summary>
        internal static void Reset()
        {
            ultimoErrorPorClave.Clear();
        }
    }
}
