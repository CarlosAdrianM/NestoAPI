using NestoAPI.Models;
using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>
    /// NestoAPI#520: implementación con SQL parametrizado sobre NovedadesVotos y NovedadesComentarios
    /// (Scripts/Issue520_FeedbackNovedades.sql). Cada columna se lee con el tipo exacto del DTO.
    /// </summary>
    public class ServicioFeedbackNovedades : IServicioFeedbackNovedades
    {
        public List<ResumenFeedbackNovedad> LeerResumen(IEnumerable<int> novedades, string usuario)
        {
            List<int> ids = (novedades ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (!ids.Any())
            {
                return new List<ResumenFeedbackNovedad>();
            }
            // Los ids son int: se pueden componer en el IN sin riesgo de inyección.
            string lista = string.Join(",", ids);
            string sql = @"
                SELECT n.Id AS NovedadId,
                       CAST(ISNULL(v.Positivos, 0) AS int) AS Positivos,
                       CAST(ISNULL(v.Negativos, 0) AS int) AS Negativos,
                       CAST(ISNULL(c.Numero, 0) AS int) AS Comentarios,
                       mv.Voto AS MiVoto
                FROM Novedades n
                LEFT JOIN (SELECT NovedadId,
                                  SUM(CASE WHEN Voto = 1 THEN 1 ELSE 0 END) AS Positivos,
                                  SUM(CASE WHEN Voto = -1 THEN 1 ELSE 0 END) AS Negativos
                           FROM NovedadesVotos GROUP BY NovedadId) v ON v.NovedadId = n.Id
                LEFT JOIN (SELECT NovedadId, COUNT(*) AS Numero
                           FROM NovedadesComentarios WHERE Borrado = 0 GROUP BY NovedadId) c ON c.NovedadId = n.Id
                LEFT JOIN NovedadesVotos mv ON mv.NovedadId = n.Id AND mv.Usuario = @usuario
                WHERE n.Id IN (" + lista + ")";
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<ResumenFeedbackNovedad>(sql,
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 128) { Value = (object)usuario ?? DBNull.Value })
                    .ToList();
            }
        }

        public bool ExisteNovedad(int novedadId)
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<int>("SELECT COUNT(*) FROM Novedades WHERE Id = @id AND Publicada = 1",
                    new SqlParameter("@id", novedadId)).Single() > 0;
            }
        }

        public void Votar(int novedadId, string usuario, string cliente, short voto)
        {
            using (var db = new NVEntities())
            {
                if (voto == 0)
                {
                    db.Database.ExecuteSqlCommand("DELETE FROM NovedadesVotos WHERE NovedadId = @id AND Usuario = @usuario",
                        new SqlParameter("@id", novedadId),
                        new SqlParameter("@usuario", SqlDbType.NVarChar, 128) { Value = usuario });
                    return;
                }
                // Upsert atómico: la UNIQUE (NovedadId, Usuario) garantiza un voto por persona.
                db.Database.ExecuteSqlCommand(@"
                    UPDATE NovedadesVotos SET Voto = @voto, Cliente = @cliente, Fecha = SYSDATETIME()
                    WHERE NovedadId = @id AND Usuario = @usuario;
                    IF @@ROWCOUNT = 0
                        INSERT INTO NovedadesVotos (NovedadId, Usuario, Cliente, Voto)
                        VALUES (@id, @usuario, @cliente, @voto);",
                    new SqlParameter("@id", novedadId),
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 128) { Value = usuario },
                    new SqlParameter("@cliente", SqlDbType.VarChar, 20) { Value = cliente },
                    new SqlParameter("@voto", SqlDbType.SmallInt) { Value = voto });
            }
        }

        public List<ComentarioNovedadDTO> LeerComentarios(int novedadId, string usuario)
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<ComentarioNovedadDTO>(@"
                    SELECT Id, NovedadId, NombreVisible, Cliente, VersionCliente, Texto, Fecha,
                           CAST(CASE WHEN Imagen IS NULL THEN 0 ELSE 1 END AS bit) AS TieneImagen,
                           CAST(CASE WHEN Usuario = @usuario THEN 1 ELSE 0 END AS bit) AS EsMio
                    FROM NovedadesComentarios
                    WHERE NovedadId = @id AND Borrado = 0
                    ORDER BY Fecha, Id",
                    new SqlParameter("@id", novedadId),
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 128) { Value = (object)usuario ?? DBNull.Value })
                    .ToList();
            }
        }

        public int CrearComentario(ComentarioNovedadAGrabar comentario)
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<int>(@"
                    INSERT INTO NovedadesComentarios (NovedadId, Usuario, NombreVisible, Cliente, VersionCliente, Texto, Imagen, ImagenTipo)
                    OUTPUT CAST(INSERTED.Id AS int)
                    VALUES (@id, @usuario, @nombre, @cliente, @version, @texto, @imagen, @tipo);",
                    new SqlParameter("@id", comentario.NovedadId),
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 128) { Value = comentario.Usuario },
                    new SqlParameter("@nombre", SqlDbType.NVarChar, 100) { Value = comentario.NombreVisible },
                    new SqlParameter("@cliente", SqlDbType.VarChar, 20) { Value = comentario.Cliente },
                    new SqlParameter("@version", SqlDbType.VarChar, 30) { Value = (object)comentario.VersionCliente ?? DBNull.Value },
                    new SqlParameter("@texto", SqlDbType.NVarChar, 2000) { Value = comentario.Texto },
                    new SqlParameter("@imagen", SqlDbType.VarBinary, -1) { Value = (object)comentario.Imagen ?? DBNull.Value },
                    new SqlParameter("@tipo", SqlDbType.VarChar, 30) { Value = (object)comentario.ImagenTipo ?? DBNull.Value })
                    .Single();
            }
        }

        public ImagenComentarioNovedad LeerImagen(int comentarioId)
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<ImagenComentarioNovedad>(
                    "SELECT Imagen, ImagenTipo FROM NovedadesComentarios WHERE Id = @id AND Borrado = 0 AND Imagen IS NOT NULL",
                    new SqlParameter("@id", comentarioId)).FirstOrDefault();
            }
        }

        public List<AutorComentarioNovedad> LeerAutores(IEnumerable<int> comentarios)
        {
            List<int> ids = (comentarios ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (!ids.Any())
            {
                return new List<AutorComentarioNovedad>();
            }
            // Los ids son int: se pueden componer en el IN sin riesgo de inyección.
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<AutorComentarioNovedad>(
                    "SELECT Id, NovedadId, Usuario, NombreVisible, Cliente FROM NovedadesComentarios " +
                    "WHERE Borrado = 0 AND Id IN (" + string.Join(",", ids) + ")").ToList();
            }
        }

        public string LeerAutorComentario(int comentarioId)
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<string>(
                    "SELECT Usuario FROM NovedadesComentarios WHERE Id = @id AND Borrado = 0",
                    new SqlParameter("@id", comentarioId)).FirstOrDefault();
            }
        }

        public void BorrarComentario(int comentarioId)
        {
            using (var db = new NVEntities())
            {
                db.Database.ExecuteSqlCommand("UPDATE NovedadesComentarios SET Borrado = 1 WHERE Id = @id",
                    new SqlParameter("@id", comentarioId));
            }
        }

        public FeedbackNovedadesDTO LeerFeedback(DateTime desde, bool soloNoRevisados)
        {
            using (var db = new NVEntities())
            {
                var feedback = new FeedbackNovedadesDTO { Desde = desde };
                feedback.Comentarios = db.Database.SqlQuery<ComentarioFeedbackDTO>(@"
                    SELECT c.Id, c.NovedadId, n.Version, n.Titulo, c.Usuario, c.NombreVisible, c.Cliente,
                           c.VersionCliente, c.Texto,
                           CAST(CASE WHEN c.Imagen IS NULL THEN 0 ELSE 1 END AS bit) AS TieneImagen,
                           c.Fecha, c.Revisado, c.IssueGitHub
                    FROM NovedadesComentarios c
                    JOIN Novedades n ON n.Id = c.NovedadId
                    WHERE c.Borrado = 0 AND c.Fecha >= @desde AND (@solo = 0 OR c.Revisado = 0)
                    ORDER BY c.Fecha",
                    new SqlParameter("@desde", SqlDbType.DateTime2) { Value = desde },
                    new SqlParameter("@solo", SqlDbType.Bit) { Value = soloNoRevisados }).ToList();
                feedback.VotosNegativos = db.Database.SqlQuery<VotoNegativoFeedbackDTO>(@"
                    SELECT v.NovedadId, n.Version, n.Titulo, v.Usuario, v.Cliente, v.Fecha
                    FROM NovedadesVotos v
                    JOIN Novedades n ON n.Id = v.NovedadId
                    WHERE v.Voto = -1 AND v.Fecha >= @desde
                    ORDER BY v.Fecha",
                    new SqlParameter("@desde", SqlDbType.DateTime2) { Value = desde }).ToList();
                return feedback;
            }
        }

        public bool MarcarRevisado(int comentarioId)
        {
            using (var db = new NVEntities())
            {
                return db.Database.ExecuteSqlCommand("UPDATE NovedadesComentarios SET Revisado = 1 WHERE Id = @id",
                    new SqlParameter("@id", comentarioId)) > 0;
            }
        }
    }
}
