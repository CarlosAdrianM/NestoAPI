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
using System.Data.SqlClient;
using System.IO;
using System.Linq;


namespace NestoAPI.Infraestructure.Buscador
{
    public static class LuceneBuscador
    {
        private static readonly LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;
        private static readonly string _luceneIndexDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lucene_index");

        /// <summary>
        /// El nombre sin stemming ni sinónimos (<see cref="SpanishExactoAnalyzer"/>): la palabra
        /// tal cual la escribe el usuario, para que "vapore" ponga por delante al producto que se
        /// llama "Vapore" y no lo mezcle con los sesenta que llevan "vapor".
        /// </summary>
        internal const string CAMPO_NOMBRE_EXACTO = "NombreExacto";

        /// <summary>
        /// Posición en ClasificacionMasVendidos (1 = el que más se vende; 0 = sin clasificar).
        /// Va en doc values, no en el texto: solo sirve para ponderar la puntuación.
        /// </summary>
        internal const string CAMPO_POSICION_MAS_VENDIDO = "PosicionMasVendido";

        /// <summary>
        /// El nombre convertido a cómo suena (<see cref="ClaveFoneticaEspanola"/>). Solo se consulta
        /// en el rescate, cuando escribiendo tal cual no hay ningún resultado: es lo que permite
        /// encontrar "ricchezza" a quien escribe "rikeza", que está a tres letras y por tanto fuera
        /// del alcance del difuso.
        /// </summary>
        internal const string CAMPO_NOMBRE_FONETICO = "NombreFonetico";

        public static void IndexarTodo()
        {
            Indexar(_luceneIndexDirectory, ObtenerProductos(), ObtenerVideos());
        }

        // Internal para tests (InternalsVisibleTo("NestoAPI.Tests")): recibe la ruta del índice y
        // los datos ya leídos, para poder indexar en una carpeta temporal sin tocar la base de datos.
        internal static void Indexar(string rutaIndice, List<ResultadoBusqueda> productos, List<(int Id, string Protocolo, string Transcripcion, string Nombre)> videos)
        {
            IndexWriterConfig indexConfig = new IndexWriterConfig(AppLuceneVersion, CrearAnalizadorDeIndexado());

            using (FSDirectory dir = FSDirectory.Open(rutaIndice))
            using (IndexWriter writer = new IndexWriter(dir, indexConfig))
            {
                writer.DeleteAll();

                foreach (var producto in productos)
                {
                    Document doc = new Document
                    {
                        new StringField("Tipo", "producto", Field.Store.YES),
                        new StringField("Id", producto.Id, Field.Store.YES),
                        new StringField("Anulado", producto.Anulado ? "true" : "false", Field.Store.YES),
                        new TextField("Nombre", producto.Nombre, Field.Store.YES) { Boost = 4.0f },
                        new TextField(CAMPO_NOMBRE_EXACTO, producto.Nombre, Field.Store.NO),
                        new TextField(CAMPO_NOMBRE_FONETICO, producto.Nombre, Field.Store.NO),
                        new NumericDocValuesField(CAMPO_POSICION_MAS_VENDIDO, producto.PosicionMasVendido ?? 0),
                        new TextField("Familia", producto.Familia ?? "", Field.Store.YES) { Boost = 3.0f },
                        new TextField("Subgrupo", producto.Subgrupo ?? "", Field.Store.YES) { Boost = 3.0f },
                        // La referencia también va en el texto: "17404" tiene que encontrar el producto
                        // (la caja del footer de la tienda, Nesto y la app buscan por referencia).
                        new TextField("TextoCompleto", $"{producto.Id} {producto.Nombre} {producto.Familia} {producto.Subgrupo} {producto.DescripcionBreve} {QuitarHtml(producto.DescripcionLarga)}", Field.Store.NO),
                    };
                    writer.AddDocument(doc);
                }

                foreach ((int Id, string Protocolo, string Transcripcion, string Nombre) in videos)
                {
                    string protocoloLimpio = QuitarHtml(Protocolo);
                    Document doc = new Document
                    {
                        new StringField("Tipo", "video", Field.Store.YES),
                        new StringField("Id", Id.ToString(), Field.Store.YES),
                        new TextField("Protocolo", protocoloLimpio, Field.Store.NO) {Boost = 2.0f },
                        new TextField("TextoCompleto", $"{protocoloLimpio} {QuitarTiempos(Transcripcion)}", Field.Store.NO),
                        // OJO: TextField, igual que el nombre de los productos, y NUNCA StringField.
                        // StringField lleva OmitNorms y DOCS_ONLY, y en Lucene 4 eso se pega al
                        // campo entero del segmento: el Nombre de los 35.000 productos perdía el
                        // boost, la normalización por longitud y la frecuencia, y el ranking
                        // quedaba decidido solo por TextoCompleto (03/09/26: "vapore" sacaba la
                        // Vapore la 26ª, detrás de todos los vasos de vapor).
                        new TextField("Nombre", Nombre, Field.Store.YES) { Boost = 4.0f },
                        new TextField(CAMPO_NOMBRE_FONETICO, Nombre, Field.Store.NO)
                    };
                    writer.AddDocument(doc);
                }

                writer.Commit();
            }
        }

        // Cada campo con su analizador: el nombre exacto no se stemea; el resto, como siempre.
        private static Analyzer CrearAnalizadorDeIndexado()
        {
            return new PerFieldAnalyzerWrapper(
                new SpanishInsensitiveAnalyzer(AppLuceneVersion),
                new Dictionary<string, Analyzer>
                {
                    { CAMPO_NOMBRE_EXACTO, new SpanishExactoAnalyzer(AppLuceneVersion) },
                    { CAMPO_NOMBRE_FONETICO, new SpanishFoneticoAnalyzer(AppLuceneVersion) }
                });
        }

        public static List<dynamic> Buscar(string q, string tipo = null, int skip = 0, int take = 20, bool usarOperadorAND = false, bool incluirAnulados = false)
        {
            return Buscar(new ParametrosBusqueda
            {
                Query = q,
                Tipo = tipo,
                Skip = skip,
                Take = take,
                Operador = usarOperadorAND ? OperadorBusqueda.AND : OperadorBusqueda.OR,
                IncluirAnulados = incluirAnulados
            });
        }

        public static List<dynamic> Buscar(ParametrosBusqueda parametros)
        {
            return BuscarEnIndice(_luceneIndexDirectory, parametros);
        }

        /// <summary>
        /// La página pedida y cuántos resultados hay en total, para que quien pagina (el buscador
        /// de la tienda PrestaShop) sepa si hay más. <see cref="ResultadoPaginado.Total"/> cuenta
        /// los activos (la consulta principal); los anulados, si se piden, van en
        /// <see cref="ResultadoPaginado.TotalAnulados"/> y detrás de los activos en la lista.
        /// </summary>
        public static ResultadoPaginado BuscarPaginado(ParametrosBusqueda parametros)
        {
            return BuscarPaginadoEnIndice(_luceneIndexDirectory, parametros);
        }

        // Internal para tests (InternalsVisibleTo("NestoAPI.Tests")): recibe la ruta del índice
        // para poder buscar sobre un índice temporal.
        internal static List<dynamic> BuscarEnIndice(string rutaIndice, ParametrosBusqueda parametros)
        {
            return BuscarPaginadoEnIndice(rutaIndice, parametros).Resultados;
        }

        internal static ResultadoPaginado BuscarPaginadoEnIndice(string rutaIndice, ParametrosBusqueda parametros)
        {
            SpanishInsensitiveAnalyzer analyzer = new SpanishInsensitiveAnalyzer(AppLuceneVersion);

            using (FSDirectory dir = FSDirectory.Open(rutaIndice))
            using (IndexReader reader = DirectoryReader.Open(dir))
            {
                IndexSearcher searcher = new IndexSearcher(reader);
                int maximo = parametros.Skip + parametros.Take;

                List<dynamic> resultados = Ejecutar(searcher, ConstruirQuery(parametros, analyzer, soloAnulados: false), maximo, out int totalActivos);
                int totalAnulados = 0;

                if (parametros.IncluirAnulados)
                {
                    // Los anulados van SIEMPRE detrás de los activos: la tienda los pinta
                    // colapsados al final ("Ver N productos anulados").
                    resultados.AddRange(Ejecutar(searcher, ConstruirQuery(parametros, analyzer, soloAnulados: true), maximo, out totalAnulados));
                }

                // Rescate difuso: si escribiendo bien no hay NADA, se reintenta admitiendo erratas
                // ("richeza" -> "ricchezza"). Solo cuando la búsqueda normal se queda a cero, para
                // que quien escribe bien no vea nunca un resultado peor por culpa del difuso.
                if (totalActivos == 0 && totalAnulados == 0 && HayPalabraParaDifusa(parametros.Query))
                {
                    resultados = Ejecutar(searcher, ConstruirQuery(parametros, analyzer, soloAnulados: false, difusa: true), maximo, out totalActivos);
                    if (parametros.IncluirAnulados)
                    {
                        resultados.AddRange(Ejecutar(searcher, ConstruirQuery(parametros, analyzer, soloAnulados: true, difusa: true), maximo, out totalAnulados));
                    }
                }

                return new ResultadoPaginado
                {
                    Total = totalActivos,
                    TotalAnulados = totalAnulados,
                    Resultados = resultados.Skip(parametros.Skip).Take(parametros.Take).ToList()
                };
            }
        }

        private static Query ConstruirQuery(ParametrosBusqueda parametros, SpanishInsensitiveAnalyzer analyzer, bool soloAnulados, bool difusa = false)
        {
            string[] campos = new[] { "TextoCompleto", "Nombre", "Protocolo" };
            MultiFieldQueryParser parser = new MultiFieldQueryParser(AppLuceneVersion, campos, analyzer)
            {
                DefaultOperator = parametros.Operador == OperadorBusqueda.AND ? Operator.AND : Operator.OR,
                // Las dos primeras letras tienen que coincidir: acota el número de términos que
                // Lucene tiene que comparar (rápido) y evita que "cera" case con "vera" o "sera".
                FuzzyPrefixLength = PREFIJO_DIFUSA
            };

            string escapedQuery = QueryParser.Escape(parametros.Query);
            if (difusa)
            {
                escapedQuery = ConsultaDifusa(escapedQuery);
            }

            // El texto de siempre O la referencia exacta. Cada palabra de la consulta que parezca
            // una referencia se busca también como término exacto sobre Id con un boost alto, para
            // que el producto con esa referencia salga el primero aunque otros la mencionen en su
            // descripción (la referencia va además en TextoCompleto, que es lo que hace que se
            // encuentre; el término sobre Id es lo que la pone en cabeza).
            BooleanQuery textoOReferencia = new BooleanQuery
            {
                { parser.Parse(escapedQuery), Occur.SHOULD }
            };
            foreach (string referencia in PalabrasQueParecenReferencia(parametros.Query))
            {
                textoOReferencia.Add(new TermQuery(new Term("Id", referencia)) { Boost = BOOST_REFERENCIA_EXACTA }, Occur.SHOULD);
            }

            // Y la palabra tal cual sobre el nombre, sin stemming: "vapore" suma aquí solo para
            // el producto que se llama "Vapore"; "vapor" sigue encontrándolo por los campos de
            // siempre. Es un SHOULD más: no quita resultados, solo reordena.
            QueryParser parserExacto = new QueryParser(AppLuceneVersion, CAMPO_NOMBRE_EXACTO, new SpanishExactoAnalyzer(AppLuceneVersion))
            {
                DefaultOperator = parser.DefaultOperator,
                FuzzyPrefixLength = PREFIJO_DIFUSA
            };
            Query nombreExacto = parserExacto.Parse(escapedQuery);
            nombreExacto.Boost = BOOST_NOMBRE_EXACTO;
            textoOReferencia.Add(nombreExacto, Occur.SHOULD);

            if (difusa)
            {
                // Cómo suena lo que ha escrito: rescata lo que el difuso no alcanza porque son
                // más de dos letras de diferencia ("rikeza" -> "ricchezza"). Va con la consulta
                // ORIGINAL, sin las marcas "~": la clave fonética ya iguala las grafías.
                QueryParser parserFonetico = new QueryParser(AppLuceneVersion, CAMPO_NOMBRE_FONETICO, new SpanishFoneticoAnalyzer(AppLuceneVersion))
                {
                    DefaultOperator = parser.DefaultOperator
                };
                Query fonetica = parserFonetico.Parse(QueryParser.Escape(parametros.Query));
                fonetica.Boost = BOOST_NOMBRE_EXACTO;
                textoOReferencia.Add(fonetica, Occur.SHOULD);
            }

            BooleanQuery query = new BooleanQuery
            {
                { textoOReferencia, Occur.MUST }
            };

            if (!string.IsNullOrEmpty(parametros.Tipo))
            {
                query.Add(new TermQuery(new Term("Tipo", parametros.Tipo.ToLower())), Occur.MUST);
            }

            // Los vídeos no llevan el campo Anulado, así que los activos se filtran excluyendo los
            // anulados (MUST_NOT) y no exigiendo "Anulado:false", que dejaría fuera a los vídeos.
            TermQuery esAnulado = new TermQuery(new Term("Anulado", "true"));
            query.Add(esAnulado, soloAnulados ? Occur.MUST : Occur.MUST_NOT);

            return new PonderadorMasVendidos(query);
        }

        private const float BOOST_REFERENCIA_EXACTA = 50f;
        private const float BOOST_NOMBRE_EXACTO = 3f;

        /// <summary>
        /// Cuánto sube la puntuación de texto un producto por lo que se vende. Con 1, el que más
        /// se vende dobla su puntuación; el último de la clasificación y los sin clasificar (y los
        /// vídeos) se quedan como están. Va con el logaritmo de la posición: entre el 50 y el 500
        /// hay más diferencia que entre el 5.000 y el 5.450, que es como se reparten las ventas.
        /// </summary>
        internal const float PESO_MAS_VENDIDOS = 1f;

        // A partir de aquí la posición ya no suma nada. Hoy hay ~36.400 productos clasificados.
        private const double POSICION_HORIZONTE = 50000;

        /// <summary>Multiplicador de la puntuación de texto según la posición en más vendidos. Internal para tests.</summary>
        internal static float FactorMasVendido(long posicion)
        {
            if (posicion <= 0 || posicion >= POSICION_HORIZONTE)
            {
                return 1f;
            }
            return (float)(1 + PESO_MAS_VENDIDOS * (1 - Math.Log(posicion) / Math.Log(POSICION_HORIZONTE)));
        }

        /// <summary>
        /// Multiplica la puntuación de texto de Lucene por <see cref="FactorMasVendido"/>: un
        /// producto que se vende mucho adelanta a los de relevancia parecida, pero no aparece en
        /// búsquedas que no le tocan, porque no se le añade puntuación: se le multiplica la suya.
        /// </summary>
        private class PonderadorMasVendidos : CustomScoreQuery
        {
            public PonderadorMasVendidos(Query subQuery) : base(subQuery) { }

            protected override CustomScoreProvider GetCustomScoreProvider(AtomicReaderContext context)
            {
                return new Proveedor(context);
            }

            private class Proveedor : CustomScoreProvider
            {
                private readonly NumericDocValues _posiciones;

                public Proveedor(AtomicReaderContext context) : base(context)
                {
                    // Null si ningún documento del segmento lleva el campo (un índice solo de vídeos)
                    _posiciones = context.AtomicReader.GetNumericDocValues(CAMPO_POSICION_MAS_VENDIDO);
                }

                public override float CustomScore(int doc, float subQueryScore, float valSrcScore)
                {
                    long posicion = _posiciones == null ? 0 : _posiciones.Get(doc);
                    return subQueryScore * FactorMasVendido(posicion);
                }
            }
        }

        /// <summary>
        /// A partir de cuántas letras se admite una errata. Con menos, casi cualquier palabra está
        /// a dos ediciones de casi cualquier otra y el rescate traería ruido en vez de ayuda.
        /// </summary>
        internal const int MIN_LONGITUD_DIFUSA = 4;

        /// <summary>Letras iniciales que tienen que coincidir sí o sí en una búsqueda difusa.</summary>
        private const int PREFIJO_DIFUSA = 2;

        /// <summary>
        /// La misma consulta pidiéndole a Lucene que admita erratas: cada palabra lo bastante larga
        /// se marca con "~" (hasta dos letras de diferencia, que es el máximo de Lucene). Así
        /// "richeza" encuentra "ricchezza". Las palabras cortas y lo que ya lleva sintaxis de
        /// consulta (comillas, campos, comodines) se dejan tal cual. Internal para tests.
        /// </summary>
        internal static string ConsultaDifusa(string consulta)
        {
            if (string.IsNullOrWhiteSpace(consulta))
            {
                return consulta;
            }
            IEnumerable<string> palabras = consulta
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                .Select(palabra => AdmiteErrata(palabra) ? palabra + "~" : palabra);
            return string.Join(" ", palabras);
        }

        private static bool AdmiteErrata(string palabra)
        {
            // Solo palabras: un número es una referencia o una medida, y ahí una errata no es una
            // errata (buscar el 17404 no puede traer el 17405).
            return palabra.Length >= MIN_LONGITUD_DIFUSA && palabra.All(char.IsLetter);
        }

        /// <summary>¿Tiene la consulta alguna palabra a la que merezca la pena admitirle erratas?</summary>
        internal static bool HayPalabraParaDifusa(string consulta)
        {
            return !string.IsNullOrWhiteSpace(consulta)
                && consulta.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Any(AdmiteErrata);
        }

        /// <summary>
        /// Las palabras de la consulta que pueden ser una referencia de producto: entre 3 y 10
        /// caracteres, solo letras y números (el Id se indexa tal cual, recortado). Internal para tests.
        /// </summary>
        internal static IEnumerable<string> PalabrasQueParecenReferencia(string consulta)
        {
            return (consulta ?? "")
                .Replace(',', ' ').Replace(';', ' ').Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length >= 3 && p.Length <= 10 && p.All(char.IsLetterOrDigit))
                .Distinct();
        }

        private static List<dynamic> Ejecutar(IndexSearcher searcher, Query query, int maximo, out int total)
        {
            List<dynamic> resultados = new List<dynamic>();

            // Lucene exige pedir al menos 1 documento; con maximo <= 0 solo interesa el total
            TopDocs topDocs = searcher.Search(query, maximo < 1 ? 1 : maximo);
            total = topDocs.TotalHits;

            if (maximo <= 0)
            {
                return resultados;
            }

            ScoreDoc[] hits = topDocs.ScoreDocs;

            foreach (ScoreDoc hit in hits)
            {
                Document doc = searcher.Doc(hit.Doc);
                resultados.Add(new
                {
                    Tipo = doc.Get("Tipo"),
                    Id = doc.Get("Id"),
                    Nombre = doc.Get("Nombre"),
                    Familia = doc.Get("Familia"),
                    Anulado = doc.Get("Anulado") == "true"
                });
            }

            return resultados;
        }

        private static string QuitarHtml(string html)
        {
            return System.Text.RegularExpressions.Regex.Replace(html ?? "", "<.*?>", " ");
        }

        private static string QuitarTiempos(string texto)
        {
            return System.Text.RegularExpressions.Regex.Replace(texto ?? "", "\\d{1,2}:\\d{2}", "");
        }

        private static List<ResultadoBusqueda> ObtenerProductos()
        {
            var resultado = new List<ResultadoBusqueda>();

            using (NVEntities db = new NVEntities())
            {
                string cadenaConexion = db.Database.Connection.ConnectionString;

                using (SqlConnection conexion = new SqlConnection(cadenaConexion))
                {
                    using (SqlCommand comando = new SqlCommand(@"
                        SELECT 
                            p.Número AS Id,
                            ISNULL(NULLIF(LTRIM(RTRIM(pp.Nombre)), ''), LTRIM(RTRIM(p.Nombre))) AS Nombre,
                            ISNULL(pp.DescripciónBreve, '') AS DescripcionBreve,
                            ISNULL(pp.Descripción, '') AS DescripcionLarga,
                            ISNULL(rtrim(f.Descripción), '') AS Familia,
	                        ISNULL(rtrim(s.Descripción), '') AS Subgrupo,
                            ISNULL(p.Estado, 0) AS Estado,
                            c.Posicion AS PosicionMasVendido
                        FROM Productos p INNER JOIN Familias f
                        on f.Empresa = p.Empresa and f.Número = p.Familia
                        INNER JOIN SubGruposProducto s
                        on s.Empresa = p.Empresa and s.Grupo = p.Grupo and s.Número = p.SubGrupo
                        LEFT JOIN PrestashopProductos pp
                            ON p.Empresa = pp.Empresa AND p.Número = pp.Número
                        LEFT JOIN ClasificacionMasVendidos c
                            ON c.Empresa = p.Empresa AND c.Producto = p.Número
                        WHERE p.Empresa = '1' and p.Grupo != 'MTP' and p.Subgrupo != 'MMP'
                        ", conexion))
                    {
                        conexion.Open();
                        using (SqlDataReader lector = comando.ExecuteReader())
                        {
                            while (lector.Read())
                            {
                                string id = lector.GetString(0);
                                string nombre = lector.IsDBNull(1) ? "" : lector.GetString(1);
                                string descripcionBreve = lector.IsDBNull(2) ? "" : lector.GetString(2);
                                string descripcionLarga = lector.IsDBNull(3) ? "" : lector.GetString(3);
                                string familia = lector.IsDBNull(4) ? "" : lector.GetString(4);
                                string subgrupo = lector.IsDBNull(5) ? "" : lector.GetString(5);
                                short estado = lector.GetInt16(6);
                                int? posicionMasVendido = lector.IsDBNull(7) ? (int?)null : lector.GetInt32(7);

                                resultado.Add(new ResultadoBusqueda
                                {
                                    Tipo = "producto",
                                    Id = id.Trim(),
                                    Nombre = nombre,
                                    Familia = familia,
                                    Subgrupo = subgrupo,
                                    DescripcionBreve = descripcionBreve,
                                    DescripcionLarga = descripcionLarga,
                                    Anulado = estado < 0,
                                    PosicionMasVendido = posicionMasVendido
                                });
                            }

                            return resultado;
                        }
                    }
                }
            }
        }


        // Ojo al orden: la fila se añade como (id, protocolo, transcripcion, nombre), que es el que
        // espera Indexar. Los nombres del tuple decían otra cosa y no coincidían con el contenido.
        private static List<(int Id, string Protocolo, string Transcripcion, string Nombre)> ObtenerVideos()
        {
            List<(int Id, string Protocolo, string Transcripcion, string Nombre)> resultado = new List<(int, string, string, string)>();

            using (NVEntities db = new NVEntities())
            {
                string cadenaConexion = db.Database.Connection.ConnectionString;

                using (SqlConnection conexion = new SqlConnection(cadenaConexion))
                {
                    using (SqlCommand comando = new SqlCommand(@"
                            SELECT Id, Protocolo, Transcripcion, Titulo 
                            FROM Videos", conexion))
                    {
                        conexion.Open();
                        using (SqlDataReader lector = comando.ExecuteReader())
                        {
                            while (lector.Read())
                            {
                                int id = lector.GetInt32(0);
                                string protocolo = lector.IsDBNull(1) ? "" : lector.GetString(1);
                                string transcripcion = lector.IsDBNull(2) ? "" : lector.GetString(2);
                                string nombre = lector.IsDBNull(3) ? "" : lector.GetString(3);

                                resultado.Add((id, protocolo, transcripcion, nombre));
                            }

                            return resultado;
                        }
                    }
                }
            }
        }

        public static List<ProductoResultadoBusqueda> BuscarProductos(string textoBusqueda, bool usarBusquedaConAND = false)
        {
            List<dynamic> resultadosGenericos = Buscar(new ParametrosBusqueda
            {
                Query = textoBusqueda,
                Tipo = "producto",
                Operador = usarBusquedaConAND ? OperadorBusqueda.AND : OperadorBusqueda.OR
            });
            return resultadosGenericos
                .Select(r =>
                {
                    return new ProductoResultadoBusqueda { Id = r.Id };
                })
                .ToList();
        }

        public static List<VideoResultadoBusqueda> BuscarVideos(string textoBusqueda, int skip = 0, int take = 20)
        {
            List<dynamic> resultadosGenericos = Buscar(textoBusqueda, "video", skip, take);
            return resultadosGenericos
                .Select(r =>
                {
                    int.TryParse(r.Id, out int id); // Si falla, id será 0 (mejor que explotar)
                    return new VideoResultadoBusqueda { Id = id };
                })
                .Where(r => r.Id != 0) // Por si acaso hubo errores de conversión
                .ToList();
        }

        public class ResultadoBusqueda
        {
            public string Tipo { get; set; }
            public string Id { get; set; }
            public string Nombre { get; set; }
            public string Subgrupo { get; set; }
            public string Familia { get; set; }
            public string DescripcionBreve { get; set; }
            public string DescripcionLarga { get; set; }
            public bool Anulado { get; set; }

            /// <summary>Posición en ClasificacionMasVendidos (1 = el que más). Null si no está clasificado.</summary>
            public int? PosicionMasVendido { get; set; }
        }


        public class ProductoResultadoBusqueda
        {
            public string Id { get; set; }
        }

        public class VideoResultadoBusqueda
        {
            public int Id { get; set; }
        }

        public enum OperadorBusqueda
        {
            AND,
            OR
        }

        /// <summary>Una página de resultados y los totales que permiten saber si hay más.</summary>
        public class ResultadoPaginado
        {
            /// <summary>Resultados activos que casan con la búsqueda (sin contar los anulados).</summary>
            public int Total { get; set; }

            /// <summary>Anulados que casan; 0 si no se pidieron (IncluirAnulados=false).</summary>
            public int TotalAnulados { get; set; }

            public List<dynamic> Resultados { get; set; } = new List<dynamic>();
        }

        public class ParametrosBusqueda
        {
            public string Query { get; set; }
            public string Tipo { get; set; }
            public int Skip { get; set; } = 0;
            public int Take { get; set; } = 20;
            public OperadorBusqueda Operador { get; set; } = OperadorBusqueda.OR;

            /// <summary>
            /// Por defecto false: los clientes que ya existían (Nesto, NestoApp) siguen sin ver
            /// productos anulados. La tienda los pide con true para poder mostrarlos etiquetados.
            /// </summary>
            public bool IncluirAnulados { get; set; } = false;
        }
    }
}
