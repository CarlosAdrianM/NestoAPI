using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>
    /// NestoAPI#165 / #728: los métodos de pago que se pueden fijar en DS_MERCHANT_PAYMETHODS de
    /// Redsys (y en PagosTPV.MetodoPago). No se pueden combinar: un formulario firmado = un
    /// método. Null = sin fijar (Redsys enseña todo lo que tenga habilitado el terminal y la
    /// página de pago ofrece todos los botones).
    ///
    /// <para>Para la contabilidad da igual cuál sea: Apple Pay y Google Pay son pagos con tarjeta
    /// (la del wallet) por el mismo terminal, así que la notificación sigue el mismo camino que
    /// tarjeta y Bizum (ServicioPagos.ProcesarNotificacion no mira el método).</para>
    /// </summary>
    public static class MetodosPagoRedsys
    {
        public const string TARJETA = "C";
        public const string BIZUM = "z";
        public const string APPLE_PAY = "xpay";
        public const string GOOGLE_PAY = "google";

        /// <summary>El orden en el que salen los botones en la página de pago.</summary>
        public static readonly IReadOnlyList<string> TODOS = new[] { TARJETA, BIZUM, APPLE_PAY, GOOGLE_PAY };

        /// <summary>
        /// Deja el método como lo espera Redsys. "C" y "z" distinguen mayúsculas (son códigos de
        /// Redsys), así que solo se pasan a minúsculas los nuevos, que son palabras. Null o en
        /// blanco = sin fijar.
        /// </summary>
        public static string Normalizar(string metodo)
        {
            if (string.IsNullOrWhiteSpace(metodo))
            {
                return null;
            }
            string limpio = metodo.Trim();
            if (string.Equals(limpio, APPLE_PAY, StringComparison.OrdinalIgnoreCase))
            {
                return APPLE_PAY;
            }
            if (string.Equals(limpio, GOOGLE_PAY, StringComparison.OrdinalIgnoreCase))
            {
                return GOOGLE_PAY;
            }
            return limpio;
        }

        /// <summary>Null (sin fijar) o uno de los cuatro métodos.</summary>
        public static bool EsValido(string metodo)
        {
            string normalizado = Normalizar(metodo);
            return normalizado == null || TODOS.Contains(normalizado);
        }

        /// <summary>
        /// Apple Pay y Google Pay no dejan un token de tarjeta reutilizable (como Bizum), así que
        /// no se pide tokenizar cuando el pago va fijado a uno de ellos (#178). Con el resto no
        /// cambia nada respecto a antes de #728.
        /// </summary>
        public static bool PermiteTokenizar(string metodo)
        {
            string normalizado = Normalizar(metodo);
            return normalizado != APPLE_PAY && normalizado != GOOGLE_PAY;
        }

        /// <summary>
        /// #728: Apple Pay solo se completa en dispositivos de Apple (iPhone, iPad, iPod y Mac; el
        /// iPad con iPadOS se presenta como "Macintosh"). Igual que el módulo de la tienda.
        /// </summary>
        public static bool EsDispositivoApple(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent))
            {
                return false;
            }
            return new[] { "iPhone", "iPad", "iPod", "Macintosh", "Mac OS X" }
                .Any(m => userAgent.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Los botones que pinta la página de pago, en orden.
        /// <list type="bullet">
        /// <item>Sin método fijado: tarjeta, Bizum y Google Pay siempre; Apple Pay solo en
        /// dispositivos de Apple.</item>
        /// <item>Con método fijado (la app ya lo eligió): solo ese, para no ofrecer cambio tras
        /// una decisión explícita (#165). Excepción: Apple Pay fijado fuera de un dispositivo
        /// de Apple no se puede completar, así que se ofrecen los demás en su lugar.</item>
        /// </list>
        /// </summary>
        public static IReadOnlyList<string> AMostrar(string metodoFijado, string userAgent)
        {
            bool esApple = EsDispositivoApple(userAgent);
            string fijado = Normalizar(metodoFijado);

            if (fijado != null && TODOS.Contains(fijado) && (fijado != APPLE_PAY || esApple))
            {
                return new[] { fijado };
            }

            return TODOS.Where(m => m != APPLE_PAY || esApple).ToList();
        }
    }
}
