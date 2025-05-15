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
            SpanishInsensitiveAnalyzer analyzer = new SpanishInsensitiveAnalyzer(AppLuceneVersion);
            IndexWriterConfig indexConfig = new IndexWriterConfig(AppLuceneVersion, analyzer);

            using (FSDirectory dir = FSDirectory.Open(_luceneIndexDirectory))
            using (IndexWriter writer = new IndexWriter(dir, indexConfig))
            {
                writer.DeleteAll();

                List<ResultadoBusqueda> productos = ObtenerProductos();

                foreach (var producto in productos)
                {
                    Document doc = new Document
                    {
                        new StringField("Tipo", "producto", Field.Store.YES),
                        new StringField("Id", producto.Id, Field.Store.YES),
                        new TextField("Nombre", producto.Nombre, Field.Store.YES) { Boost = 4.0f },
                        new TextField("Familia", producto.Familia ?? "", Field.Store.YES) { Boost = 3.0f },
                        new TextField("Subgrupo", producto.Subgrupo ?? "", Field.Store.YES) { Boost = 3.0f },
                        new TextField("TextoCompleto", $"{producto.Nombre} {producto.Familia} {producto.Subgrupo} {producto.DescripcionBreve} {QuitarHtml(producto.DescripcionLarga)}", Field.Store.NO),
                    };
                    writer.AddDocument(doc);
                }

                List<(int Id, string Protocolo, string Transcripcion, string Nombre)> videos = ObtenerVideos();
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

        public static List<dynamic> Buscar(string q, string tipo = null, int skip = 0, int take = 20)
        {
            SpanishInsensitiveAnalyzer analyzer = new SpanishInsensitiveAnalyzer(AppLuceneVersion);

            using (FSDirectory dir = FSDirectory.Open(_luceneIndexDirectory))
            using (IndexReader reader = DirectoryReader.Open(dir))
            {
                IndexSearcher searcher = new IndexSearcher(reader);

                string[] campos = new[] { "TextoCompleto", "Nombre", "Protocolo" };
                MultiFieldQueryParser parser = new MultiFieldQueryParser(AppLuceneVersion, campos, analyzer)
                {
                    DefaultOperator = Operator.AND
                };

                string escapedQuery = QueryParser.Escape(q);
                Query query = parser.Parse(escapedQuery);

                if (!string.IsNullOrEmpty(tipo))
                {
                    TermQuery filtro = new TermQuery(new Term("Tipo", tipo.ToLower()));
                    query = new BooleanQuery
            {
                { query, Occur.MUST },
                { filtro, Occur.MUST }
            };
                }

                ScoreDoc[] hits = searcher.Search(query, skip + take).ScoreDocs;

                List<dynamic> resultados = new List<dynamic>();

                foreach (ScoreDoc hit in hits.Skip(skip).Take(take))
                {
                    Document doc = searcher.Doc(hit.Doc);
                    resultados.Add(new
                    {
                        Tipo = doc.Get("Tipo"),
                        Id = doc.Get("Id"),
                        Nombre = doc.Get("Nombre")
                    });
                }

                return resultados;
            }
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
	                        ISNULL(rtrim(s.Descripción), '') AS Subgrupo
                        FROM Productos p INNER JOIN Familias f
                        on f.Empresa = p.Empresa and f.Número = p.Familia
                        INNER JOIN SubGruposProducto s
                        on s.Empresa = p.Empresa and s.Grupo = p.Grupo and s.Número = p.SubGrupo
                        LEFT JOIN PrestashopProductos pp 
                            ON p.Empresa = pp.Empresa AND p.Número = pp.Número
                        WHERE p.Empresa = '1' and p.Estado >= 0 and p.Grupo != 'MTP' and p.Subgrupo != 'MMP'
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

                                resultado.Add(new ResultadoBusqueda
                                {
                                    Tipo = "producto",
                                    Id = id.Trim(),
                                    Nombre = nombre,
                                    Familia = familia,
                                    Subgrupo = subgrupo,
                                    DescripcionBreve = descripcionBreve,
                                    DescripcionLarga = descripcionLarga
                                });
                            }

                            return resultado;
                        }
                    }
                }
            }
        }


        private static List<(int Id, string Nombre, string Protocolo, string Transcripcion)> ObtenerVideos()
        {
            List<(int Id, string Nombre, string Protocolo, string Transcripcion)> resultado = new List<(int, string, string, string)>();

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

        public static List<ProductoResultadoBusqueda> BuscarProductos(string textoBusqueda)
        {
            List<dynamic> resultadosGenericos = Buscar(textoBusqueda, "producto");
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
        }


        public class ProductoResultadoBusqueda
        {
            public string Id { get; set; }
        }

        public class VideoResultadoBusqueda
        {
            public int Id { get; set; }
        }
    }
}
