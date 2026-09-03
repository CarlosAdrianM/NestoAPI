using System.Globalization;
using System.Text;

namespace NestoAPI.Infraestructure.Buscador
{
    /// <summary>
    /// Cómo suena una palabra escrita por alguien de aquí. Reduce a la misma clave las grafías que
    /// en español se pronuncian igual, para que el buscador encuentre "ricchezza" a quien escribe
    /// "rikeza": b/v, c/s/z (seseo), g/j, k/qu/c, ll/y, la hache muda y la e- inicial de "esplendor".
    ///
    /// <para>No es un codificador fonético al uso (Soundex, Metaphone y compañía están pensados
    /// para apellidos ingleses y fallan justo en esto: probados los siete de Lucene el 03/09/26,
    /// ninguno juntaba "gel" con "jel" ni "bomba" con "vomba" sin juntar además "cera" con "vera").
    /// Aquí las reglas son las del español y se leen de un vistazo.</para>
    ///
    /// <para>Solo se usa en el rescate, cuando la búsqueda normal no encuentra nada, así que
    /// confundir "cera" con "sera" es aceptable: la alternativa es no devolver nada.</para>
    /// </summary>
    public static class ClaveFoneticaEspanola
    {
        // Marcador temporal para la "ch", que es un sonido propio y no debe perder la hache
        private const char CH = '';

        public static string Calcular(string palabra)
        {
            if (string.IsNullOrWhiteSpace(palabra))
            {
                return string.Empty;
            }

            string s = QuitarTildes(palabra.Trim().ToLowerInvariant());
            s = QuitarEProteticaInicial(s);

            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                char siguiente = i + 1 < s.Length ? s[i + 1] : '\0';
                bool siguienteEsEI = siguiente == 'e' || siguiente == 'i';

                switch (c)
                {
                    case 'c':
                        // "cch" italiana ("ricchezza") suena a k; "ch" es un sonido propio;
                        // "ce"/"ci" son seseo; el resto, k.
                        if (siguiente == 'c' && i + 2 < s.Length && s[i + 2] == 'h')
                        {
                            _ = sb.Append('k');
                            i += 2;
                        }
                        else if (siguiente == 'h')
                        {
                            _ = sb.Append(CH);
                            i++;
                        }
                        else
                        {
                            _ = sb.Append(siguienteEsEI ? 's' : 'k');
                        }
                        break;
                    case 'q':
                        // "que"/"qui": la u es muda
                        _ = sb.Append('k');
                        if (siguiente == 'u')
                        {
                            i++;
                        }
                        break;
                    case 'g':
                        if (siguienteEsEI)
                        {
                            _ = sb.Append('j'); // "gel" = "jel"
                        }
                        else if (siguiente == 'u' && i + 2 < s.Length && (s[i + 2] == 'e' || s[i + 2] == 'i'))
                        {
                            _ = sb.Append('g'); // "guerra": la u es muda
                            i++;
                        }
                        else
                        {
                            _ = sb.Append('g');
                        }
                        break;
                    case 'l':
                        if (siguiente == 'l')
                        {
                            _ = sb.Append('y'); // yeísmo: "mascarilla" = "mascariya"
                            i++;
                        }
                        else
                        {
                            _ = sb.Append('l');
                        }
                        break;
                    case 'z':
                        _ = sb.Append('s'); // seseo
                        break;
                    case 'v':
                    case 'w':
                        _ = sb.Append('b');
                        break;
                    case 'x':
                        _ = sb.Append('s');
                        break;
                    case 'h':
                        break; // muda
                    case 'y':
                        // "y" sola es la conjunción; entre letras suena como ll
                        _ = sb.Append(s.Length == 1 ? 'i' : 'y');
                        break;
                    default:
                        _ = sb.Append(c);
                        break;
                }
            }

            return Restaurar(ColapsarRepetidas(sb.ToString()));
        }

        /// <summary>La e- que se le añade delante a las palabras que empiezan por s + consonante.</summary>
        private static string QuitarEProteticaInicial(string s)
        {
            return s.Length >= 3 && s[0] == 'e' && s[1] == 's' && EsConsonante(s[2]) ? s.Substring(1) : s;
        }

        private static bool EsConsonante(char c)
        {
            return char.IsLetter(c) && "aeiou".IndexOf(c) < 0;
        }

        /// <summary>Las letras dobles suenan una sola vez ("ricchezza" ya convertida, "innovar").</summary>
        private static string ColapsarRepetidas(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (sb.Length == 0 || sb[sb.Length - 1] != c)
                {
                    _ = sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string Restaurar(string s)
        {
            return s.Replace(CH.ToString(), "ch");
        }

        private static string QuitarTildes(string texto)
        {
            string normalizado = texto.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder(normalizado.Length);
            foreach (char c in normalizado)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    _ = sb.Append(c);
                }
            }
            // La eñe se pierde al quitar tildes y no es lo mismo que una n, pero para buscar vale
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
