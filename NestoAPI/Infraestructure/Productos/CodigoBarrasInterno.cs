using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Productos
{
    /// <summary>
    /// NestoAPI#762 (sugerencia 909, Carlos 30/10/26): el código de barras INTERNO de un producto que no tiene ninguno, para que
    /// todo se pueda recibir escaneando. Es un EAN-13 del rango 20-29 de GS1 (circulación restringida: ningún fabricante lo usa)
    /// y, dentro de él, del <b>29</b>, que en la BD no usaba nadie (30/10/26: hay 99 códigos que empiezan por 2, en 20x-27x).
    /// <list type="bullet">
    /// <item><c>290</c> + la referencia con ceros a 9 cifras + dígito de control: 17404 → 2900000174047. Se ve la referencia en
    /// el número de debajo y no hace falta contador: dos peticiones a la vez dan el mismo código.</item>
    /// <item><c>299</c> + un contador de 9 cifras + control, para las referencias que no son números de hasta 9 cifras
    /// (MF0581, 11-31907…) o si el de la referencia ya lo tuviera otro producto (no debería pasar).</item>
    /// </list>
    /// Se guarda como un código más en ProductosCodigosBarras (Origen Almacen) y NUNCA es el principal: no va a la ficha
    /// (Productos.CodBarras), que lee PrestaShop y manda a Google y Amazon como GTIN.
    /// </summary>
    public static class CodigoBarrasInterno
    {
        public const string PREFIJO = "29";
        public const string PREFIJO_REFERENCIA = "290";
        public const string PREFIJO_CONTADOR = "299";
        public const int CIFRAS_REFERENCIA = 9;
        public const int LONGITUD = 13;

        /// <summary>True si es uno de los nuestros: 13 cifras, empieza por 29 y el dígito de control cuadra.</summary>
        public static bool EsInterno(string codigo)
        {
            string limpio = codigo?.Trim();
            return limpio != null && limpio.Length == LONGITUD && limpio.StartsWith(PREFIJO, StringComparison.Ordinal)
                && ValidadorGtin.EsCodigoValido(limpio);
        }

        /// <summary>El código que corresponde a la referencia (290 + 9 cifras + control), o null si no es un número de 1 a 9 cifras.</summary>
        public static string DeReferencia(string producto)
        {
            string limpio = producto?.Trim();
            if (string.IsNullOrEmpty(limpio) || limpio.Length > CIFRAS_REFERENCIA || !limpio.All(c => c >= '0' && c <= '9'))
            {
                return null;
            }
            if (limpio.All(c => c == '0'))
            {
                return null;
            }
            return ConControl(PREFIJO_REFERENCIA + limpio.PadLeft(CIFRAS_REFERENCIA, '0'));
        }

        /// <summary>El código del contador <paramref name="numero"/> (299 + 9 cifras + control).</summary>
        public static string DeContador(long numero)
        {
            if (numero < 1 || numero > 999999999)
            {
                throw new ArgumentOutOfRangeException(nameof(numero), "El contador de códigos internos va de 1 a 999.999.999.");
            }
            return ConControl(PREFIJO_CONTADOR + numero.ToString("D9", CultureInfo.InvariantCulture));
        }

        /// <summary>El siguiente del contador, visto lo que ya hay (los que no son del contador no cuentan).</summary>
        public static string SiguienteDelContador(IEnumerable<string> existentes)
        {
            long mayor = 0;
            foreach (string codigo in existentes ?? Enumerable.Empty<string>())
            {
                string limpio = codigo?.Trim();
                if (EsInterno(limpio) && limpio.StartsWith(PREFIJO_CONTADOR, StringComparison.Ordinal)
                    && long.TryParse(limpio.Substring(PREFIJO_CONTADOR.Length, CIFRAS_REFERENCIA), NumberStyles.None, CultureInfo.InvariantCulture, out long n)
                    && n > mayor)
                {
                    mayor = n;
                }
            }
            return DeContador(mayor + 1);
        }

        /// <summary>Las 12 cifras con su dígito de control GS1.</summary>
        internal static string ConControl(string doceCifras)
            => doceCifras + ValidadorGtin.DigitoControl(doceCifras).ToString(CultureInfo.InvariantCulture);
    }
}
