using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Es;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.Synonym;
using Lucene.Net.Analysis.Util;
using Lucene.Net.Util;
using System.IO;

namespace NestoAPI.Infraestructure.Buscador
{
    /// <summary>
    /// Analizador personalizado que es insensible a los acentos
    /// </summary>
    public class SpanishInsensitiveAnalyzer : Analyzer
    {
        private readonly LuceneVersion _version;
        private readonly SynonymMap _synonymMap;

        private static readonly LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;
        private static CharArraySet SpanishStopWords => new CharArraySet(AppLuceneVersion, SpanishStopWordsArray, true);

        public SpanishInsensitiveAnalyzer(LuceneVersion version)
        {
            _version = version;
            _synonymMap = BuildSynonyms();
        }

        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
        {
            // Primero usamos el analizador estándar para tokenización básica
            StandardTokenizer tokenizer = new StandardTokenizer(_version, reader);

            // Convertimos a minúsculas
            TokenStream tokenStream = new LowerCaseFilter(_version, tokenizer);

            // Aplicamos el filtro de stopwords en español
            tokenStream = new StopFilter(_version, tokenStream, SpanishStopWords);

            // Eliminamos los acentos/diacríticos
            tokenStream = new ASCIIFoldingFilter(tokenStream);

            // Aplicamos el filtro de sinónimos
            tokenStream = new SynonymFilter(tokenStream, _synonymMap, true);

            // Reducimos las palabras a su raíz
            tokenStream = new SpanishLightStemFilter(tokenStream);

            return new TokenStreamComponents(tokenizer, tokenStream);
        }

        private SynonymMap BuildSynonyms()
        {
            var builder = new SynonymMap.Builder(true); // true = dedupe (elimina duplicados)

            var sinonimos = new[]
            {
                new[] { "láser", "depilación láser", "fotodepilación", "foto depilación", "diodo", "ipl" },
                new[] { "radiofrecuencia", "rf", "radio frecuencia" },
                new[] { "hidrafacial", "hydrafacial", "hidra facial", "hydra facial" },
                new[] { "dermapen", "skinpen", "derma pen", "skin pen", "microneedling", "micropunciones" },
                new[] { "ampolla", "vial", "ampolleta", "cóctel", "cocktail", "concentrado" },
                new[] { "pelo", "cabello" },
                new[] { "calentador", "fundidor", "olla" },
                new[] { "rubor", "colorete" },
                new[] { "pintalabios", "labial", "lipstick", "pinta labios", "barra de labios", "carmín", "gloss" },
                new[] { "esmalte", "pintauñas", "pinta uñas" },
                new[] { "bobina", "rollo" },
                new[] { "aluminio", "papel plata" },
                new[] { "serum", "suero", "booster", "fluido" },
                new[] { "pañuelos faciales", "tissues", "kleenex" },
                new[] { "pyruvic", "pirúvico" },
            };

            foreach (var grupo in sinonimos)
            {
                AddMutualSynonyms(builder, grupo);
            }

            return builder.Build();
        }

        private void AddMutualSynonyms(SynonymMap.Builder builder, params string[] terms)
        {
            for (int i = 0; i < terms.Length - 1; i++)
            {
                for (int j = i + 1; j < terms.Length; j++)
                {
                    AddBidirectional(builder, terms[i], terms[j]);
                }
            }
        }

        private void AddBidirectional(SynonymMap.Builder builder, string a, string b)
        {
            AddUnidirectional(builder, a, b);
            AddUnidirectional(builder, b, a);
        }

        private void AddUnidirectional(SynonymMap.Builder builder, string from, string to)
        {
            builder.Add(new CharsRef(from), new CharsRef(to), true);
        }

        private static readonly string[] SpanishStopWordsArray = new string[]
        {
            "a", "al", "algo", "algunas", "algunos", "ante", "antes", "como", "con", "contra", "cual",
            "cuando", "de", "del", "desde", "donde", "durante", "e", "el", "ella", "ellas", "ellos",
            "en", "entre", "era", "erais", "eran", "eras", "eres", "es", "esa", "esas", "ese", "eso",
            "esos", "esta", "estaba", "estado", "estamos", "estan", "estar", "estará", "estas", "este",
            "esto", "estos", "estoy", "etc", "fin", "fue", "fueron", "fui", "fuimos", "ha", "hace",
            "haceis", "hacemos", "hacen", "hacer", "hacia", "han", "has", "hasta", "hay", "haya",
            "he", "hecho", "hemos", "hizo", "hubo", "la", "las", "le", "les", "lo", "los", "me",
            "mi", "mis", "mucho", "muchos", "muy", "nada", "ni", "no", "nos", "nosotros", "o", "os",
            "otra", "otros", "para", "pero", "podeis", "podemos", "poder", "por", "porque", "que",
            "quien", "quienes", "qué", "se", "sea", "sean", "ser", "si", "sí", "sin", "sobre", "so",
            "son", "su", "sus", "tal", "también", "tanto", "te", "teneis", "tenemos", "tener", "todos",
            "tu", "tus", "un", "una", "uno", "unos", "vosotros", "y", "ya"
        };

    }
}
