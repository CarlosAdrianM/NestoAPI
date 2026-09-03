using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;
using System.IO;

namespace NestoAPI.Infraestructure.Buscador
{
    /// <summary>
    /// Analizador para la palabra tal cual: minúsculas y sin acentos, pero SIN stemming, sin
    /// stopwords y sin sinónimos. Existe porque <see cref="SpanishInsensitiveAnalyzer"/> reduce
    /// "vapore" a "vapor", así que con él "vapore" y "vapor" son la misma búsqueda y el producto
    /// que se llama exactamente "Vapore" no tiene forma de destacar sobre los sesenta que llevan
    /// "vapor" en el nombre. Se usa para indexar y consultar el campo NombreExacto.
    /// </summary>
    public class SpanishExactoAnalyzer : Analyzer
    {
        private readonly LuceneVersion _version;

        public SpanishExactoAnalyzer(LuceneVersion version)
        {
            _version = version;
        }

        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
        {
            StandardTokenizer tokenizer = new StandardTokenizer(_version, reader);
            TokenStream tokenStream = new LowerCaseFilter(_version, tokenizer);
            tokenStream = new ASCIIFoldingFilter(tokenStream);
            return new TokenStreamComponents(tokenizer, tokenStream);
        }
    }
}
