using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>
    /// NestoAPI#537: @menciones en los comentarios de Novedades. Uso principal: cuando una conversación
    /// con el asistente se atasca, se menciona a @Carlos (o a quien sea) y le llega el aviso.
    /// Puras para testear sin BD.
    /// </summary>
    public static class ReglasMenciones
    {
        // @ al principio o tras algo que no sea letra, dígito, punto o @ (así «pepe@nuevavision.es» no es
        // una mención) y un nombre que empieza por letra.
        private static readonly Regex PATRON = new Regex(@"(?<![\p{L}\p{Nd}.@])@([\p{L}][\p{L}\p{Nd}_]*)", RegexOptions.Compiled);

        /// <summary>Los nombres mencionados, normalizados (sin tildes, en mayúsculas) y sin repetir.</summary>
        public static List<string> Extraer(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return new List<string>();
            }
            return PATRON.Matches(texto).Cast<Match>()
                .Select(m => Normalizar(m.Groups[1].Value))
                .Distinct()
                .ToList();
        }

        /// <summary>Sin tildes ni mayúsculas: «@maría» menciona a «Maria».</summary>
        public static string Normalizar(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return string.Empty;
            }
            string descompuesto = nombre.Trim().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    _ = sb.Append(c);
                }
            }
            return sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        }

        /// <summary>
        /// Los mencionados que existen entre los mencionables del ámbito, sin quien escribe y como mucho
        /// uno por persona. Un nombre que no está (o que casa con dos) no avisa a nadie.
        /// </summary>
        public static List<MencionableDTO> Resolver(IEnumerable<string> menciones, IEnumerable<MencionableDTO> mencionables, string claveAutor)
        {
            List<MencionableDTO> candidatos = (mencionables ?? Enumerable.Empty<MencionableDTO>()).ToList();
            var resultado = new List<MencionableDTO>();
            foreach (string mencion in menciones ?? Enumerable.Empty<string>())
            {
                List<MencionableDTO> encontrados = candidatos.Where(m => Normalizar(m.Nombre) == mencion).ToList();
                if (encontrados.Count != 1)
                {
                    continue;
                }
                MencionableDTO mencionado = encontrados[0];
                if (string.Equals(mencionado.Clave, claveAutor, StringComparison.OrdinalIgnoreCase)
                    || resultado.Any(r => string.Equals(r.Clave, mencionado.Clave, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                resultado.Add(mencionado);
            }
            return resultado;
        }
    }
}
