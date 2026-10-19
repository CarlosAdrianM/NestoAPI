using System.Linq;

namespace NestoAPI.Infraestructure.Productos
{
    /// <summary>
    /// NestoAPI#674 (Carlos, 19/10/26): un código de barras de producto solo vale si es un GTIN de verdad: EAN-8, UPC-A (12),
    /// EAN-13 o ITF-14/GTIN-14, todo cifras y con el dígito de control bien. En la ficha había «1», «0», letras y códigos de
    /// relleno que casaban con lecturas que no eran de ese producto. Un código que no pasa no casa con nada (al leer) y no se
    /// guarda (al grabar); el producto cuenta como «sin código». Las referencias internas («44724») no son GTIN: se buscan
    /// como referencia, no por aquí.
    /// </summary>
    public static class ValidadorGtin
    {
        public const string MENSAJE_CODIGO_NO_VALIDO = "El código {0} no es un código de barras válido: compruebe las cifras.";

        /// <summary>True si es un EAN-8, UPC-A, EAN-13 o GTIN-14 con el dígito de control correcto (admite espacios alrededor).</summary>
        public static bool EsCodigoValido(string codigo)
        {
            string limpio = codigo?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return false;
            }
            if (limpio.Length != 8 && limpio.Length != 12 && limpio.Length != 13 && limpio.Length != 14)
            {
                return false;
            }
            if (!limpio.All(c => c >= '0' && c <= '9'))
            {
                return false;
            }
            // Todo ceros pasa el dígito de control, pero no es el código de ningún producto: es relleno
            if (limpio.All(c => c == '0'))
            {
                return false;
            }
            return DigitoControl(limpio.Substring(0, limpio.Length - 1)) == limpio[limpio.Length - 1] - '0';
        }

        /// <summary>El código recortado si es válido; null si no lo es (o no hay): así cuenta como «sin código».</summary>
        public static string SoloSiValido(string codigo)
        {
            return EsCodigoValido(codigo) ? codigo.Trim() : null;
        }

        /// <summary>El mensaje de usted para un código que no se puede guardar.</summary>
        public static string MensajeNoValido(string codigo)
        {
            return string.Format(MENSAJE_CODIGO_NO_VALIDO, codigo?.Trim());
        }

        /// <summary>
        /// Dígito de control GS1 (módulo 10): desde la derecha, las cifras pesan 3, 1, 3, 1… Es la misma regla para las
        /// cuatro longitudes.
        /// </summary>
        internal static int DigitoControl(string sinControl)
        {
            int suma = 0;
            for (int i = 0; i < sinControl.Length; i++)
            {
                int cifra = sinControl[sinControl.Length - 1 - i] - '0';
                suma += i % 2 == 0 ? cifra * 3 : cifra;
            }
            return (10 - suma % 10) % 10;
        }
    }
}
