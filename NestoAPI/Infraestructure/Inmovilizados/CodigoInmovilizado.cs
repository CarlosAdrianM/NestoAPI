using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Inmovilizados
{
    /// <summary>
    /// Etiquetas de inmovilizado (Carlos 25/11/26): LA REGLA (pura) de la nomenclatura de la casa: «I/1823» = la letra I, una barra y
    /// el número del inmovilizado SIN relleno. Es lo que se imprime en la etiqueta y lo que lleva su código de barras (Code128).
    /// <para>Al leer se admite «I/1823», «i/1823» y espacios alrededor o junto a la barra («I / 1823»), con SOLO cifras detrás. El
    /// número a secas («1823») NO es un código de inmovilizado: es la referencia de un producto y así se sigue tratando.</para>
    /// </summary>
    public static class CodigoInmovilizado
    {
        public const string PREFIJO = "I/";

        private static readonly Regex formato = new Regex(@"^\s*[Ii]\s*/\s*(\d{1,10})\s*$", RegexOptions.Compiled);

        /// <summary>«1823» (o «1823      », con el relleno del char(10)) → «I/1823». Null si no hay número.</summary>
        public static string Componer(string numero)
        {
            string limpio = Normalizar(numero);
            return limpio == null ? null : PREFIJO + limpio;
        }

        /// <summary>
        /// El número del inmovilizado de lo leído («I/1823» → «1823», sin ceros a la izquierda), o null si no tiene el formato de
        /// inmovilizado (también el número a secas, que es una referencia de producto).
        /// </summary>
        public static string Leer(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            Match m = formato.Match(texto);
            return m.Success ? Normalizar(m.Groups[1].Value) : null;
        }

        /// <summary>True si lo leído tiene el formato de un código de inmovilizado («I/1823»).</summary>
        public static bool EsCodigo(string texto) => Leer(texto) != null;

        /// <summary>El número sin relleno ni ceros a la izquierda («0»: «0»). Null si no queda nada.</summary>
        internal static string Normalizar(string numero)
        {
            string limpio = numero?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            if (limpio.All(char.IsDigit))
            {
                string sinCeros = limpio.TrimStart('0');
                return sinCeros.Length == 0 ? "0" : sinCeros;
            }
            return limpio;
        }

        /// <summary>0 Normal, -1 Anulado; cualquier otro, el número.</summary>
        public static string DescripcionEstado(short estado)
        {
            switch (estado)
            {
                case 0:
                    return "Normal";
                case -1:
                    return "Anulado";
                default:
                    return estado.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
