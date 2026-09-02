using Lucene.Net.Documents;
using Lucene.Net.Index;
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

        public static void IndexarTodo()
        {
            Indexar(_luceneIndexDirectory, ObtenerProductos(), ObtenerVideos());
        }

        // Internal para tests (InternalsVisibleTo("NestoAPI.Tests")): recibe la ruta del índice y
        // los datos ya leídos, para poder indexar en una carpeta temporal sin tocar la base de datos.
        internal static void Indexar(string rutaIndice, List<ResultadoBusqueda> productos, List<(int Id, string Protocolo, string Transcripcion, string Nombre)> videos)
        {
            SpanishInsensitiveAnalyzer analyzer = new SpanishInsensitiveAnalyzer(AppLuceneVersion);
            IndexWriterConfig indexConfig = new IndexWriterConfig(AppLuceneVersion, analyzer);

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
                        new TextField("Familia", producto.Familia ?? "", Field.Store.YES) { Boost = 3.0f },
                        new TextField("Subgrupo", producto.Subgrupo ?? "", Field.Store.YES) { Boost = 3.0f },
                        new TextField("TextoCompleto", $"{producto.Nombre} {producto.Familia} {producto.Subgrupo} {producto.DescripcionBreve} {QuitarHtml(producto.DescripcionLarga)}", Field.Store.NO),
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
                        new StringField("Nombre", Nombre, Field.Store.YES)
                    };
                    writer.AddDocument(doc);
                }

                writer.Commit();
            }
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

                return new ResultadoPaginado
                {
                    Total = totalActivos,
                    TotalAnulados = totalAnulados,
                    Resultados = resultados.Skip(parametros.Skip).Take(parametros.Take).ToList()
                };
            }
        }

        private static Query ConstruirQuery(ParametrosBusqueda parametros, SpanishInsensitiveAnalyzer analyzer, bool soloAnulados)
        {
            string[] campos = new[] { "TextoCompleto", "Nombre", "Protocolo" };
            MultiFieldQueryParser parser = new MultiFieldQueryParser(AppLuceneVersion, campos, analyzer)
            {
                DefaultOperator = parametros.Operador == OperadorBusqueda.AND ? Operator.AND : Operator.OR
            };

            string escapedQuery = QueryParser.Escape(parametros.Query);
            BooleanQuery query = new BooleanQuery
            {
                { parser.Parse(escapedQuery), Occur.MUST }
            };

            if (!string.IsNullOrEmpty(parametros.Tipo))
            {
                query.Add(new TermQuery(new Term("Tipo", parametros.Tipo.ToLower())), Occur.MUST);
            }

            // Los vídeos no llevan el campo Anulado, así que los activos se filtran excluyendo los
            // anulados (MUST_NOT) y no exigiendo "Anulado:false", que dejaría fuera a los vídeos.
            TermQuery esAnulado = new TermQuery(new Term("Anulado", "true"));
            query.Add(esAnulado, soloAnulados ? Occur.MUST : Occur.MUST_NOT);

            return query;
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
                            ISNULL(p.Estado, 0) AS Estado
                        FROM Productos p INNER JOIN Familias f
                        on f.Empresa = p.Empresa and f.Número = p.Familia
                        INNER JOIN SubGruposProducto s
                        on s.Empresa = p.Empresa and s.Grupo = p.Grupo and s.Número = p.SubGrupo
                        LEFT JOIN PrestashopProductos pp
                            ON p.Empresa = pp.Empresa AND p.Número = pp.Número
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

                                resultado.Add(new ResultadoBusqueda
                                {
                                    Tipo = "producto",
                                    Id = id.Trim(),
                                    Nombre = nombre,
                                    Familia = familia,
                                    Subgrupo = subgrupo,
                                    DescripcionBreve = descripcionBreve,
                                    DescripcionLarga = descripcionLarga,
                                    Anulado = estado < 0
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
