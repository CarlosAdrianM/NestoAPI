using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Facturas.Facturae
{
    /// <summary>
    /// NestoAPI#903: el formato de un código DIR3 (oficina contable, órgano gestor, unidad tramitadora…). Son 9 caracteres:
    /// una letra (E Estado, A autonómica, L local, U universidad, I, J, O…) y el resto cifras o alguna letra más (EA0003214,
    /// GE0011893, LA0007357). Sin ser estrictos de más: 9 letras o cifras, empieza por letra y al menos 5 cifras.
    /// </summary>
    public static class ReglasDir3
    {
        public const int LONGITUD = 9;

        /// <summary>Null si vale (y <paramref name="limpio"/> en mayúsculas y sin espacios); si no, el motivo de usted.</summary>
        public static string Validar(string codigo, string que, out string limpio)
        {
            limpio = Regex.Replace(codigo ?? string.Empty, @"[\s\-\.]", string.Empty).ToUpperInvariant();
            if (limpio.Length == 0)
            {
                return $"Falta el código DIR3 {Minuscula(que)}.";
            }
            if (limpio.Length != LONGITUD || !Regex.IsMatch(limpio, "^[A-Z][A-Z0-9]{8}$") || limpio.Count(char.IsDigit) < 5)
            {
                return $"El código DIR3 {Minuscula(que)} «{codigo?.Trim()}» no es válido: son 9 caracteres, una letra y cifras (por ejemplo, L01280796).";
            }
            return null;
        }

        /// <summary>«Oficina contable» → «de la oficina contable»; «Órgano gestor» → «del órgano gestor».</summary>
        private static string Minuscula(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return "del centro";
            }
            string minuscula = char.ToLowerInvariant(texto[0]) + texto.Substring(1);
            return (minuscula.StartsWith("órgano") ? "del " : "de la ") + minuscula;
        }
    }
}
