using System;
using System.Linq;

namespace NestoAPI.Infraestructure.Agencias.Innovatrans
{
    /// <summary>
    /// Mapeo de direcciones de NestoAPI al formato DataTrans DTX (Innovatrans). Reglas
    /// confirmadas por el integrador de Innovatrans/Ingenium (16/06/26).
    /// </summary>
    public static class MapeadorDireccionDataTrans
    {
        public const string PAIS_ESPANA = "ESP";
        public const string PAIS_PORTUGAL = "PRT";

        /// <summary>
        /// Código de provincia DataTrans (3 chars) a partir del código postal.
        ///  - España/Península (y Baleares/Canarias/Ceuta/Melilla): "0" + 2 primeros dígitos del CP
        ///    (28001 -> "028", 35001 -> "035", 51001 -> "051").
        ///  - Portugal: "6" + 4 primeros dígitos del CP (1000-001 -> "61000").
        ///
        /// OJO PENDIENTE (llamada con el integrador): el campo provincia del WS es de longitud 3,
        /// pero la regla de Portugal produce 5 chars -> confirmar si Portugal va en 'provincia' o en
        /// 'nomProvinciaDestInternacional'/'codPostalInternacional'. La península (caso 99%) sí encaja.
        /// </summary>
        public static string ProvinciaDesdeCodigoPostal(string codigoPostal, string pais)
        {
            string digitos = SoloDigitos(codigoPostal);
            bool esPortugal = string.Equals(pais?.Trim(), PAIS_PORTUGAL, StringComparison.OrdinalIgnoreCase);

            if (esPortugal)
            {
                return digitos.Length < 4 ? string.Empty : "6" + digitos.Substring(0, 4);
            }

            // España (península, Baleares, Canarias, Ceuta, Melilla).
            return digitos.Length < 2 ? string.Empty : "0" + digitos.Substring(0, 2);
        }

        /// <summary>
        /// Normaliza un código postal portugués (formato oficial NNNN-NNN) venga como venga de la BD:
        /// "1000-001", "1000 001" o junto "1000001" → siempre el canónico con guion "NNNN-NNN". Si no
        /// son exactamente 7 dígitos no es un CP portugués reconocible y se devuelve tal cual (trim).
        /// </summary>
        public static string NormalizarCodigoPostalPortugal(string codigoPostal)
        {
            string digitos = SoloDigitos(codigoPostal);
            if (digitos.Length != 7)
            {
                return codigoPostal?.Trim();
            }
            return digitos.Substring(0, 4) + "-" + digitos.Substring(4, 3);
        }

        private static string SoloDigitos(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return string.Empty;
            }
            return new string(texto.Where(char.IsDigit).ToArray());
        }
    }
}
