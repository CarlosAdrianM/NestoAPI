using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Queries;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NestoAPI.Infraestructure.Buscador
{
    /// <summary>Un cliente tal y como entra en el índice.</summary>
    public class ClienteIndexable
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }

        /// <summary>Puesto por compras del último año (1 = el que más). 0 = sin ventas.</summary>
        public int PosicionVentas { get; set; }
    }

    /// <summary>Lo que devuelve el buscador: con qué cliente quedarse y en qué orden.</summary>
    public class ClaveCliente
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
    }

    /// <summary>
    /// NestoAPI#455: buscador de clientes con la misma metodología que el de productos. Índice
    /// Lucene reconstruido por las noches, tolerante a erratas y ordenado por relevancia en vez de
    /// alfabéticamente: buscando "Carlos" salen todos los Carlos, del que más compra al que menos.
    ///
    /// <para>Clase aparte de <see cref="LuceneBuscador"/> (que ya lleva productos y vídeos) pero
    /// reutilizando sus analizadores y su índice temporal propio.</para>
    ///
    /// <para><b>Devuelve números de cliente, no DTOs.</b> Quien llama sigue construyendo el
    /// <c>ClienteDTO</c> como siempre y solo respeta este orden, así que la respuesta de la API no
    /// cambia de forma y Nesto y NestoApp no se enteran.</para>
    /// </summary>
    public static class BuscadorClientes
    {
        private static readonly LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

        internal static readonly string RutaIndice =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lucene_index_clientes");

        internal const string CAMPO_EMPRESA = "Empresa";
        internal const string CAMPO_CLIENTE = "Cliente";
        internal const string CAMPO_CONTACTO = "Contacto";

        /// <summary>
        /// El número de cliente sin analizar. Se busca por término exacto: "15191" tiene que
        /// encontrar al 15191 y el 1519 no pinta nada ahí, así que nada de prefijos ni comodines.
        /// </summary>
        internal const string CAMPO_NUMERO_EXACTO = "NumeroExacto";

        internal const string CAMPO_NOMBRE = "Nombre";
        internal const string CAMPO_DIRECCION = "Direccion";
        internal const string CAMPO_CODIGO_POSTAL = "CodigoPostal";
        internal const string CAMPO_POBLACION = "Poblacion";
        internal const string CAMPO_NOMBRE_FONETICO = "NombreFonetico";
        internal const string CAMPO_POSICION_VENTAS = "PosicionVentas";

        // Pesos pedidos en la issue, de más a menos: número (absoluto), dirección, nombre, código
        // postal y población. El número no compite por boost sino por término exacto, con un peso
        // tan alto que gana siempre.
        private const float BOOST_NUMERO_EXACTO = 100f;
        private const float BOOST_DIRECCION = 4f;
        private const float BOOST_NOMBRE = 3f;
        private const float BOOST_CODIGO_POSTAL = 2f;
        private const float BOOST_POBLACION = 1.5f;

        /// <summary>
        /// Cuánto multiplica las ventas a la puntuación de texto. Con 1, el que más compra dobla
        /// su puntuación y el que no compra se queda igual. Va con el logaritmo del puesto, porque
        /// entre el 10 y el 100 hay mucha más diferencia de compra que entre el 2.000 y el 2.090.
        /// </summary>
        internal const float PESO_VENTAS = 1f;

        /// <summary>A partir de aquí el puesto ya no suma. Hoy compran unos 3.500 clientes al año.</summary>
        private const double PUESTO_HORIZONTE = 5000;

        public static void IndexarTodo()
        {
            Indexar(RutaIndice, ObtenerClientes());
        }

        // Internal para tests: indexa en una carpeta temporal con datos ya leídos, sin tocar la BD.
        internal static void Indexar(string rutaIndice, List<ClienteIndexable> clientes)
        {
            IndexWriterConfig config = new IndexWriterConfig(AppLuceneVersion, CrearAnalizadorDeIndexado());

            using (FSDirectory dir = FSDirectory.Open(rutaIndice))
            using (IndexWriter writer = new IndexWriter(dir, config))
            {
                writer.DeleteAll();

                foreach (ClienteIndexable cliente in clientes)
                {
                    string nombre = cliente.Nombre ?? string.Empty;
                    string direccion = cliente.Direccion ?? string.Empty;
                    string poblacion = cliente.Poblacion ?? string.Empty;

                    Document doc = new Document
                    {
                        new StringField(CAMPO_EMPRESA, (cliente.Empresa ?? string.Empty).Trim(), Field.Store.YES),
                        new StringField(CAMPO_CLIENTE, (cliente.Cliente ?? string.Empty).Trim(), Field.Store.YES),
                        new StringField(CAMPO_CONTACTO, (cliente.Contacto ?? string.Empty).Trim(), Field.Store.YES),
                        new StringField(CAMPO_NUMERO_EXACTO, (cliente.Cliente ?? string.Empty).Trim(), Field.Store.NO),
                        new TextField(CAMPO_NOMBRE, nombre, Field.Store.NO) { Boost = BOOST_NOMBRE },
                        new TextField(CAMPO_DIRECCION, direccion, Field.Store.NO) { Boost = BOOST_DIRECCION },
                        new TextField(CAMPO_CODIGO_POSTAL, (cliente.CodigoPostal ?? string.Empty).Trim(), Field.Store.NO) { Boost = BOOST_CODIGO_POSTAL },
                        new TextField(CAMPO_POBLACION, poblacion, Field.Store.NO) { Boost = BOOST_POBLACION },
                        new TextField(CAMPO_NOMBRE_FONETICO, nombre + " " + poblacion, Field.Store.NO),
                        new NumericDocValuesField(CAMPO_POSICION_VENTAS, cliente.PosicionVentas)
                    };

                    writer.AddDocument(doc);
                }

                writer.Commit();
            }
        }

        public static List<ClaveCliente> Buscar(string empresa, string texto, int take = 50)
        {
            return BuscarEnIndice(RutaIndice, empresa, texto, take);
        }

        internal static List<ClaveCliente> BuscarEnIndice(string rutaIndice, string empresa, string texto, int take)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return new List<ClaveCliente>();
            }

            using (Analyzer analyzer = new SpanishInsensitiveAnalyzer(AppLuceneVersion))
            using (FSDirectory dir = FSDirectory.Open(rutaIndice))
            using (IndexReader reader = DirectoryReader.Open(dir))
            {
                IndexSearcher searcher = new IndexSearcher(reader);

                List<ClaveCliente> resultados = Ejecutar(searcher,
                    ConstruirQuery(empresa, texto, analyzer, difusa: false), take);

                // Rescate difuso y fonético, SOLO si escribiendo bien no hay nada: quien escribe
                // bien no puede ver nunca un resultado peor por culpa del rescate.
                if (resultados.Count == 0)
                {
                    resultados = Ejecutar(searcher,
                        ConstruirQuery(empresa, texto, analyzer, difusa: true), take);
                }

                return resultados;
            }
        }

        private static List<ClaveCliente> Ejecutar(IndexSearcher searcher, Query query, int take)
        {
            TopDocs top = searcher.Search(query, Math.Max(take, 1));

            return top.ScoreDocs
                .Select(sd => searcher.Doc(sd.Doc))
                .Select(d => new ClaveCliente
                {
                    Cliente = d.Get(CAMPO_CLIENTE),
                    Contacto = d.Get(CAMPO_CONTACTO)
                })
                .ToList();
        }

        private static Query ConstruirQuery(string empresa, string texto, Analyzer analyzer, bool difusa)
        {
            string limpio = texto.Trim();

            BooleanQuery porTexto = new BooleanQuery();

            // El número exacto: peso absoluto. No se le pide al parser, se pone como término tal
            // cual, así que "1519" no puede colarse en la búsqueda de "15191".
            porTexto.Add(new BooleanClause(
                new TermQuery(new Term(CAMPO_NUMERO_EXACTO, limpio)) { Boost = BOOST_NUMERO_EXACTO },
                Occur.SHOULD));

            string[] campos = difusa
                ? new[] { CAMPO_NOMBRE_FONETICO, CAMPO_NOMBRE, CAMPO_DIRECCION, CAMPO_POBLACION }
                : new[] { CAMPO_DIRECCION, CAMPO_NOMBRE, CAMPO_CODIGO_POSTAL, CAMPO_POBLACION };

            MultiFieldQueryParser parser = new MultiFieldQueryParser(AppLuceneVersion, campos, analyzer)
            {
                DefaultOperator = Operator.AND
            };

            try
            {
                porTexto.Add(new BooleanClause(
                    parser.Parse(difusa ? ConDifusa(limpio) : QueryParserBase.Escape(limpio)),
                    Occur.SHOULD));
            }
            catch (ParseException)
            {
                // Un texto que Lucene no sabe interpretar no puede tumbar la búsqueda: queda el
                // número exacto, que es lo que más importa.
            }

            BooleanQuery completa = new BooleanQuery
            {
                { new PonderadorVentas(porTexto), Occur.MUST }
            };

            if (!string.IsNullOrWhiteSpace(empresa))
            {
                completa.Add(new TermQuery(new Term(CAMPO_EMPRESA, empresa.Trim())), Occur.MUST);
            }

            return completa;
        }

        /// <summary>A partir de cuántas letras se admite una errata (igual que en productos).</summary>
        internal const int MIN_LONGITUD_DIFUSA = 4;

        internal static string ConDifusa(string texto)
        {
            IEnumerable<string> palabras = QueryParserBase.Escape(texto)
                .Split(' ')
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Length >= MIN_LONGITUD_DIFUSA && !p.Any(char.IsDigit) ? p + "~" : p);

            return string.Join(" ", palabras);
        }

        /// <summary>
        /// Multiplicador de la puntuación de texto según el puesto por compras. Internal para tests.
        /// </summary>
        internal static float FactorVentas(long puesto)
        {
            if (puesto <= 0 || puesto >= PUESTO_HORIZONTE)
            {
                return 1f;
            }

            return (float)(1 + PESO_VENTAS * (1 - Math.Log(puesto) / Math.Log(PUESTO_HORIZONTE)));
        }

        /// <summary>
        /// Multiplica —no suma— la puntuación de texto por <see cref="FactorVentas"/>: un cliente
        /// que compra mucho adelanta a otro de relevancia parecida, pero no aparece en búsquedas
        /// que no le tocan.
        /// </summary>
        private class PonderadorVentas : CustomScoreQuery
        {
            public PonderadorVentas(Query subQuery) : base(subQuery) { }

            protected override CustomScoreProvider GetCustomScoreProvider(AtomicReaderContext context)
            {
                return new Proveedor(context);
            }

            private class Proveedor : CustomScoreProvider
            {
                private readonly NumericDocValues _puestos;

                public Proveedor(AtomicReaderContext context) : base(context)
                {
                    _puestos = context.AtomicReader.GetNumericDocValues(CAMPO_POSICION_VENTAS);
                }

                public override float CustomScore(int doc, float subQueryScore, float valSrcScore)
                {
                    long puesto = _puestos == null ? 0 : _puestos.Get(doc);
                    return subQueryScore * FactorVentas(puesto);
                }
            }
        }

        private static Analyzer CrearAnalizadorDeIndexado()
        {
            return new PerFieldAnalyzerWrapper(
                new SpanishInsensitiveAnalyzer(AppLuceneVersion),
                new Dictionary<string, Analyzer>
                {
                    { CAMPO_NOMBRE_FONETICO, new SpanishFoneticoAnalyzer(AppLuceneVersion) }
                });
        }

        private static List<ClienteIndexable> ObtenerClientes()
        {
            Dictionary<string, int> puestos = new RankingClientes()
                .PosicionesPorVentas(Constantes.Empresas.EMPRESA_POR_DEFECTO);

            using (NVEntities db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;

                // Solo los que puede devolver la búsqueda de hoy (Estado >= 0)
                List<ClienteIndexable> clientes = db.Clientes
                    .Where(c => c.Estado >= 0)
                    .Select(c => new ClienteIndexable
                    {
                        Empresa = c.Empresa,
                        Cliente = c.Nº_Cliente,
                        Contacto = c.Contacto,
                        Nombre = c.Nombre,
                        Direccion = c.Dirección,
                        CodigoPostal = c.CodPostal,
                        Poblacion = c.Población
                    })
                    .ToList();

                foreach (ClienteIndexable cliente in clientes)
                {
                    cliente.PosicionVentas = puestos.TryGetValue(cliente.Cliente?.Trim() ?? string.Empty, out int puesto)
                        ? puesto
                        : 0;
                }

                return clientes;
            }
        }
    }
}
