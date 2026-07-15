using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NestoAPI.Infraestructure.Agencias.Innovatrans
{
    /// <summary>
    /// NestoAPI#300: cuando un CP tiene VARIAS poblaciones en el catálogo de DataTrans, DTX no
    /// canaliza por CP a secas (codError 405 "Canalizacion incorrecta") y exige que poblacionDes
    /// coincida con el texto EXACTO de su catálogo (verificado en prod 15/07/26: cambiar "AVILÉS"
    /// por "AVILES" bastó, sin canalizarPorPoblacion). Esta clase elige la población del catálogo
    /// (BuscarPoblacion) que corresponde a la nuestra, tolerando tildes, artículos (DE/DEL),
    /// variantes ortográficas (CALA RAJADA/CALA RATJADA) y nombres compuestos
    /// (SAN SEBASTIAN/DONOSTIA-SAN SEBASTIAN).
    /// </summary>
    public static class CanalizadorPoblacionDataTrans
    {
        private static readonly HashSet<string> Articulos = new HashSet<string>
        {
            "DE", "DEL", "LA", "EL", "LOS", "LAS", "DA", "DO", "DAS", "DOS", "D"
        };

        /// <summary>
        /// Devuelve la población del catálogo que casa con la del envío, o null si ninguna es un
        /// match razonable (en ese caso NO hay que reintentar a ciegas). Las reglas van de más a
        /// menos estrictas y dentro de cada una gana la primera candidata en el orden del catálogo.
        /// </summary>
        public static string ElegirPoblacionCatalogo(string poblacionEnvio, IReadOnlyList<string> poblacionesCatalogo)
        {
            if (poblacionesCatalogo == null || poblacionesCatalogo.Count == 0 || string.IsNullOrWhiteSpace(poblacionEnvio))
            {
                return null;
            }

            // Con una sola población DTX canaliza por CP e ignora el texto (les hemos mandado
            // "IBIZA" contra su "EIVISSA" y funcionó): si solo hay una, es esa.
            if (poblacionesCatalogo.Count == 1)
            {
                return poblacionesCatalogo[0];
            }

            string objetivo = Normalizar(poblacionEnvio);
            string objetivoSinArticulos = SinArticulos(objetivo);
            if (objetivo.Length == 0)
            {
                return null;
            }

            // 1. Igualdad exacta tras normalizar (tildes, mayúsculas, separadores): AVILÉS -> AVILES.
            foreach (string candidata in poblacionesCatalogo)
            {
                if (Normalizar(candidata) == objetivo)
                {
                    return candidata;
                }
            }

            // 2. Igualdad ignorando artículos: SAN AGUSTÍN DE GUADALIX -> SAN AGUSTIN DEL GUADALIX.
            foreach (string candidata in poblacionesCatalogo)
            {
                if (SinArticulos(Normalizar(candidata)) == objetivoSinArticulos && objetivoSinArticulos.Length > 0)
                {
                    return candidata;
                }
            }

            // 3. Contención en cualquier dirección: SAN SEBASTIAN -> DONOSTIA-SAN SEBASTIAN.
            //    Se exige un mínimo de 4 caracteres para no casar por trozos insignificantes.
            if (objetivoSinArticulos.Length >= 4)
            {
                foreach (string candidata in poblacionesCatalogo)
                {
                    string candidataSinArticulos = SinArticulos(Normalizar(candidata));
                    if (candidataSinArticulos.Length >= 4 &&
                        (candidataSinArticulos.Contains(objetivoSinArticulos) || objetivoSinArticulos.Contains(candidataSinArticulos)))
                    {
                        return candidata;
                    }
                }
            }

            // 4. Tokens equivalentes con tolerancia de una letra por token (grafías próximas):
            //    CALA RAJADA -> CALA RATJADA.
            string[] tokensObjetivo = objetivoSinArticulos.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokensObjetivo.Length > 0)
            {
                foreach (string candidata in poblacionesCatalogo)
                {
                    string[] tokensCandidata = SinArticulos(Normalizar(candidata))
                        .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokensCandidata.Length == tokensObjetivo.Length &&
                        tokensObjetivo.Zip(tokensCandidata, (a, b) => a == b || Levenshtein(a, b) <= 1).All(ok => ok))
                    {
                        return candidata;
                    }
                }
            }

            return null;
        }

        // Mayúsculas, sin diacríticos (tildes, Ñ->N como hace el catálogo) y cualquier separador
        // (guiones, comas, paréntesis...) convertido a un solo espacio.
        internal static string Normalizar(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return string.Empty;
            }
            string formD = texto.ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);
            foreach (char c in formD)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }
                _ = sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }
            return string.Join(" ", sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string SinArticulos(string textoNormalizado)
        {
            return string.Join(" ", textoNormalizado
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => !Articulos.Contains(t)));
        }

        private static int Levenshtein(string a, string b)
        {
            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) { d[i, 0] = i; }
            for (int j = 0; j <= b.Length; j++) { d[0, j] = j; }
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int coste = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + coste);
                }
            }
            return d[a.Length, b.Length];
        }
    }
}
