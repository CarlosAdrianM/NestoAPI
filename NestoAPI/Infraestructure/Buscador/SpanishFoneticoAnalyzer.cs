using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;
using System.IO;

namespace NestoAPI.Infraestructure.Buscador
{
    /// <summary>
    /// Convierte cada palabra en cómo suena (<see cref="ClaveFoneticaEspanola"/>), para el campo
    /// que permite encontrar "ricchezza" escribiendo "rikeza". Sin stemming ni sinónimos: la clave
    /// ya iguala las grafías que suenan igual y no queremos encima recortar la palabra.
    /// </summary>
    public class SpanishFoneticoAnalyzer : Analyzer
    {
        private readonly LuceneVersion _version;

        public SpanishFoneticoAnalyzer(LuceneVersion version)
        {
            _version = version;
        }

        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
        {
            StandardTokenizer tokenizer = new StandardTokenizer(_version, reader);
            TokenStream tokenStream = new LowerCaseFilter(_version, tokenizer);
            tokenStream = new FiltroClaveFonetica(tokenStream);
            return new TokenStreamComponents(tokenizer, tokenStream);
        }

        private sealed class FiltroClaveFonetica : TokenFilter
        {
            private readonly ICharTermAttribute _termino;

            public FiltroClaveFonetica(TokenStream input) : base(input)
            {
                _termino = AddAttribute<ICharTermAttribute>();
            }

            public override bool IncrementToken()
            {
                if (!m_input.IncrementToken())
                {
                    return false;
                }
                string clave = ClaveFoneticaEspanola.Calcular(_termino.ToString());
                _ = _termino.SetEmpty().Append(clave);
                return true;
            }
        }
    }
}
