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

        // Código de provincia DataTrans para Portugal (fijo, confirmado por el integrador 22/06/26).
        public const string PROVINCIA_PORTUGAL = "053";

        /// <summary>
        /// Código de provincia DataTrans (3 chars) a partir del código postal.
        ///  - España/Península (y Baleares/Canarias/Ceuta/Melilla): "0" + 2 primeros dígitos del CP
        ///    (28001 -> "028", 35001 -> "035", 51001 -> "051").
        ///  - Portugal: código fijo "053" (confirmado por el integrador 22/06/26). El CP comprimido
        ///    "6" + 4 dígitos va aparte en codPostalDes (ver <see cref="CodigoPostalDestino"/>).
        /// </summary>
        public static string ProvinciaDesdeCodigoPostal(string codigoPostal, string pais)
        {
            bool esPortugal = string.Equals(pais?.Trim(), PAIS_PORTUGAL, StringComparison.OrdinalIgnoreCase);
            if (esPortugal)
            {
                return PROVINCIA_PORTUGAL;
            }

            // España (península, Baleares, Canarias, Ceuta, Melilla).
            string digitos = SoloDigitos(codigoPostal);
            return digitos.Length < 2 ? string.Empty : "0" + digitos.Substring(0, 2);
        }

        /// <summary>
        /// Código postal de destino (campo codPostalDes) según el país, regla del integrador (22/06/26):
        ///  - España: el CP tal cual (5 dígitos).
        ///  - Portugal: "6" + 4 primeros dígitos del CP (1000-001 -> "61000"). El WS de DataTrans no
        ///    admite el formato oficial NNNN-NNN; el CP portugués viaja comprimido a 5 dígitos. Si no
        ///    hay al menos 4 dígitos no es reconocible y se devuelve tal cual (trim).
        /// </summary>
        public static string CodigoPostalDestino(string codigoPostal, string pais)
        {
            bool esPortugal = string.Equals(pais?.Trim(), PAIS_PORTUGAL, StringComparison.OrdinalIgnoreCase);
            if (!esPortugal)
            {
                return codigoPostal?.Trim();
            }

            string digitos = SoloDigitos(codigoPostal);
            return digitos.Length < 4 ? codigoPostal?.Trim() : "6" + digitos.Substring(0, 4);
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
