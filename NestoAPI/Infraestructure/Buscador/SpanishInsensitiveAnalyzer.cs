using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Analysis.Standard;
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

        public SpanishInsensitiveAnalyzer(LuceneVersion version)
        {
            _version = version;
        }

        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
        {
            // Primero usamos el analizador estándar para tokenización básica
            StandardTokenizer tokenizer = new StandardTokenizer(_version, reader);

            // Convertimos a minúsculas
            TokenStream tokenStream = new LowerCaseFilter(_version, tokenizer);

            // Eliminamos los acentos/diacríticos
            tokenStream = new ASCIIFoldingFilter(tokenStream);

            return new TokenStreamComponents(tokenizer, tokenStream);
        }
    }
}
