using System;
using System.Collections.Concurrent;

namespace NestoAPI.Infraestructure.Verifactu
{
    /// <summary>
    /// NestoAPI#346: corta el ruido de los reintentos de Verifactu. Una factura atascada se
    /// reintenta en cada pasada del job y, sin esto, cada intento repite el MISMO error en ELMAH
    /// y dispara otro correo a administración (~12 al día por factura). Se recuerda el último
    /// error por clave y solo se considera "novedad" cuando el texto cambia.
    /// El estado es en memoria y un reciclaje del pool lo resetea: por eso lo que va a ELMAH pasa
    /// por <see cref="EsNovedadEnElmah"/>, que además mira si ELMAH ya tiene ese mismo mensaje de
    /// los últimos <see cref="DIAS_MEMORIA_ELMAH"/> días (06/11/26: del 30/10 al 06/11 cada factura
    /// atascada dejaba un aviso igual cada día).
    /// </summary>
    internal static class DeduplicadorErroresVerifactu
    {
        internal const int DIAS_MEMORIA_ELMAH = 7;

        private static readonly ConcurrentDictionary<string, string> ultimoErrorPorClave =
            new ConcurrentDictionary<string, string>();

        /// <summary>Quién lee ELMAH. Null (por defecto, y en los tests) = solo memoria, como antes. Lo pone Startup.</summary>
        internal static ILectorErroresElmah LectorElmah { get; set; }

        /// <summary>Reloj (UTC, como ELMAH_Error.TimeUtc); sustituible en los tests.</summary>
        internal static Func<DateTime> AhoraUtc { get; set; } = () => DateTime.UtcNow;

        /// <summary>Startup: la deduplicación de lo que va a ELMAH sobrevive al reciclaje mirando dbo.ELMAH_Error.</summary>
        internal static void UsarElmah(ILectorErroresElmah lector)
        {
            LectorElmah = lector;
        }

        /// <summary>
        /// Registra el error y devuelve true si es una novedad para esa clave (primer error o
        /// texto distinto del último registrado). False = repetición exacta, no avisar otra vez.
        /// Solo memoria: para lo que NO va a ELMAH (el resumen por correo del job).
        /// </summary>
        internal static bool EsNovedad(string clave, string error)
        {
            string texto = error ?? string.Empty;
            bool repetido = ultimoErrorPorClave.TryGetValue(clave, out string anterior) && anterior == texto;
            ultimoErrorPorClave[clave] = texto;
            return !repetido;
        }

        /// <summary>
        /// Como <see cref="EsNovedad"/>, para un error que se va a loguear en ELMAH con
        /// <paramref name="mensajeElmah"/> como Message (exactamente el texto del LogError). Si en memoria
        /// es novedad, se mira ELMAH: con ese mismo Message en los últimos 7 días no es novedad (y queda en
        /// memoria). Si la consulta falla, es novedad (se loguea, como antes).
        /// </summary>
        internal static bool EsNovedadEnElmah(string clave, string mensajeElmah)
        {
            if (!EsNovedad(clave, mensajeElmah))
            {
                return false;
            }
            ILectorErroresElmah lector = LectorElmah;
            if (lector == null)
            {
                return true;
            }
            try
            {
                return !lector.HayErrorConMensajeDesde(mensajeElmah ?? string.Empty, AhoraUtc().AddDays(-DIAS_MEMORIA_ELMAH));
            }
            catch
            {
                return true;
            }
        }

        /// <summary>Limpia la clave cuando el envío por fin funciona (o deja de proceder).</summary>
        internal static void Limpiar(string clave)
        {
            _ = ultimoErrorPorClave.TryRemove(clave, out _);
        }

        /// <summary>Solo para tests: estado limpio entre tests (sin lector de ELMAH y con el reloj de verdad).</summary>
        internal static void Reset()
        {
            ultimoErrorPorClave.Clear();
            LectorElmah = null;
            AhoraUtc = () => DateTime.UtcNow;
        }
    }
}
