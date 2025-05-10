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

                List<(string Id, string Nombre, string DescripcionBreve, string DescripcionLarga)> productos = ObtenerProductos();
                foreach ((string Id, string Nombre, string DescripcionBreve, string DescripcionLarga) in productos)
                {
                    Document doc = new Document
                    {
                        new StringField("Tipo", "producto", Field.Store.YES),
                        new StringField("Id", Id, Field.Store.YES),
                        new TextField("Nombre", Nombre, Field.Store.YES) { Boost = 4.0f },
                        new TextField("TextoCompleto", $"{Nombre} {DescripcionBreve} {QuitarHtml(DescripcionLarga)}", Field.Store.NO)
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


        public static List<dynamic> Buscar(string q, string tipo = null)
        {
            SpanishInsensitiveAnalyzer analyzer = new SpanishInsensitiveAnalyzer(AppLuceneVersion);

            using (FSDirectory dir = FSDirectory.Open(_luceneIndexDirectory))
            using (IndexReader reader = DirectoryReader.Open(dir))
            {
                IndexSearcher searcher = new IndexSearcher(reader);

                string[] campos = new[] { "TextoCompleto", "Nombre", "Protocolo" };
                MultiFieldQueryParser parser = new MultiFieldQueryParser(AppLuceneVersion, campos, analyzer)
                {
                    DefaultOperator = Operator.OR
                };

                Query query = parser.Parse(q);

                if (!string.IsNullOrEmpty(tipo))
                {
                    TermQuery filtro = new TermQuery(new Term("Tipo", tipo.ToLower()));
                    query = new BooleanQuery
            {
                { query, Occur.MUST },
                { filtro, Occur.MUST }
            };
                }

                ScoreDoc[] hits = searcher.Search(query, 20).ScoreDocs;
                List<dynamic> resultados = new List<dynamic>();

                foreach (ScoreDoc hit in hits)
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

        private static List<(string Id, string Nombre, string DescripcionBreve, string DescripcionLarga)> ObtenerProductos()
        {
            List<(string Id, string Nombre, string DescripcionBreve, string DescripcionLarga)> resultado = new List<(string Id, string Nombre, string DescripcionBreve, string DescripcionLarga)>();

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
	                        ISNULL(rtrim(f.Descripción), '') AS Familia
                        FROM Productos p INNER JOIN Familias f
                        on f.Empresa = p.Empresa and f.Número = p.Familia
                        LEFT JOIN PrestashopProductos pp 
                            ON p.Empresa = pp.Empresa AND p.Número = pp.Número
                        WHERE p.Empresa = '1' and p.Estado >= 0 and p.Grupo != 'MTP' and p.Subgrupo != 'MMP'", conexion))
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

                                if (!string.IsNullOrEmpty(familia) && familia != "Genéricos")
                                {
                                    nombre += " de " + familia;
                                }

                                resultado.Add((id.Trim(), nombre, descripcionBreve, descripcionLarga));
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

    }
}
