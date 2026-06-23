using System;
using System.Globalization;
using System.Linq;
using System.Text;

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
        /// País (3 chars) que viaja en el WS (campos paisRem/paisDes). El integrador (23/06/26)
        /// confirma que SIEMPRE debe ir "ESP", incluso para destinos de Portugal: DataTrans canaliza
        /// Portugal a través de España (la provincia "053" y el CP "6"+4 díg ya identifican el destino
        /// portugués). Mandar "PRT" hacía que DTX rechazara el envío con codError 402 "No existe
        /// agencia asociada al país". El país interno (<paramref name="paisInterno"/>) se sigue usando
        /// para derivar el CP y la provincia (ver <see cref="CodigoPostalDestino"/> /
        /// <see cref="ProvinciaDesdeCodigoPostal"/>).
        /// </summary>
        public static string PaisParaDataTrans(string paisInterno) => PAIS_ESPANA;

        /// <summary>¿El país interno es Portugal?</summary>
        public static bool EsPortugal(string pais)
            => string.Equals(pais?.Trim(), PAIS_PORTUGAL, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Población para el WS. En Portugal hay que canalizar por población
        /// (<c>canalizarPorPoblacion=true</c>) y el texto DEBE coincidir con el catálogo de DataTrans
        /// (BuscarPoblacion); las direcciones llegan como "CIUDAD-DISTRITO" y con tildes ("ÍLHAVO-AVEIRO")
        /// que DTX no reconoce → "Canalizacion incorrecta". Normalizamos a la ciudad en mayúsculas y sin
        /// tildes, quedándonos con la parte anterior a un separador "-"/","/"/" (ÍLHAVO-AVEIRO -> ILHAVO).
        /// En España se deja tal cual (no se canaliza por población).
        /// </summary>
        public static string PoblacionParaDataTrans(string poblacion, string pais)
        {
            if (!EsPortugal(pais) || string.IsNullOrWhiteSpace(poblacion))
            {
                return poblacion;
            }
            string normalizada = QuitarTildes(poblacion).ToUpperInvariant();
            int corte = normalizada.IndexOfAny(new[] { '-', ',', '/' });
            if (corte > 0)
            {
                normalizada = normalizada.Substring(0, corte);
            }
            return normalizada.Trim();
        }

        private static string QuitarTildes(string texto)
        {
            string formD = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);
            foreach (char c in formD)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    _ = sb.Append(c);
                }
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

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
