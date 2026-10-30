using NestoAPI.Models;
using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>
    /// NestoAPI#754 (sugerencia 900): la tabla NovedadesCapturas (Scripts/NovedadesCapturasABlob.sql): qué capturas
    /// tiene cada sugerencia y cada comentario y en qué blob están. Las imágenes de antes (varbinary en Novedades.Imagen
    /// y NovedadesComentarios.Imagen) se siguen leyendo como siempre hasta que se migran.
    /// </summary>
    public interface IServicioCapturasNovedades
    {
        /// <summary>Si ya existe la tabla (mientras no, todo funciona como antes: una imagen en varbinary).</summary>
        bool Disponible();

        /// <summary>Las capturas de esas sugerencias (no las de sus comentarios), en UNA consulta. Vacía sin tabla.</summary>
        List<CapturaNovedadFila> LeerDeNovedades(IEnumerable<int> novedades);

        /// <summary>Las capturas de esos comentarios, en UNA consulta. Vacía sin tabla.</summary>
        List<CapturaNovedadFila> LeerDeComentarios(IEnumerable<int> comentarios);

        /// <summary>Una captura (null si no existe, si su novedad no está publicada o si su comentario está borrado).</summary>
        CapturaNovedadFila Leer(int capturaId);

        /// <summary>Registra las capturas ya subidas, todas o ninguna, con Orden a continuación de las que hubiera.</summary>
        List<CapturaNovedadFila> Crear(int novedadId, int? comentarioId, IReadOnlyList<CapturaSubida> capturas, string usuario);

        /// <summary>Cuántas imágenes quedan en varbinary (sugerencias y comentarios, borrados incluidos).</summary>
        ResumenImagenesPendientes ContarPendientes();

        /// <summary>Las primeras <paramref name="maximo"/> imágenes que quedan en varbinary, con su contenido.</summary>
        List<ImagenPendienteMigrar> LeerPendientes(int maximo);

        /// <summary>
        /// En una transacción: registra la captura (si no estaba ya, por la ruta) y vacía la columna Imagen. false si la
        /// imagen ya no estaba (otra pasada se adelantó): no se toca nada.
        /// </summary>
        bool RegistrarMigrada(ImagenPendienteMigrar imagen, CapturaSubida subida);
    }

    public class ServicioCapturasNovedades : IServicioCapturasNovedades
    {
        private const string SI_EXISTE_LA_TABLA = "IF OBJECT_ID(N'dbo.NovedadesCapturas', N'U') IS NOT NULL ";
        private const string COLUMNAS = "cap.Id, cap.NovedadId, cap.ComentarioId, cap.Ruta, cap.Tipo, cap.Bytes, cap.Orden";
        // Sin tabla, el mismo resultado vacío (con columnas, para que SqlQuery lo materialice)
        private const string SI_NO_VACIO =
            " ELSE SELECT CAST(0 AS int) AS Id, CAST(0 AS int) AS NovedadId, CAST(NULL AS int) AS ComentarioId, " +
            "CAST(NULL AS varchar(400)) AS Ruta, CAST(NULL AS varchar(30)) AS Tipo, CAST(0 AS int) AS Bytes, CAST(0 AS int) AS Orden WHERE 1 = 0";

        // Una vez que existe, existe: no se vuelve a preguntar a la BD
        private static volatile bool tablaVista;

        public bool Disponible()
        {
            if (tablaVista)
            {
                return true;
            }
            using (var db = new NVEntities())
            {
                bool existe = db.Database.SqlQuery<int>(
                    "SELECT CASE WHEN OBJECT_ID(N'dbo.NovedadesCapturas', N'U') IS NULL THEN 0 ELSE 1 END").Single() == 1;
                tablaVista = existe;
                return existe;
            }
        }

        public List<CapturaNovedadFila> LeerDeNovedades(IEnumerable<int> novedades)
        {
            List<int> ids = (novedades ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (ids.Count == 0)
            {
                return new List<CapturaNovedadFila>();
            }
            // Los ids son int: se pueden componer en el IN sin riesgo de inyección.
            return Consultar(SI_EXISTE_LA_TABLA + "SELECT " + COLUMNAS + " FROM dbo.NovedadesCapturas cap " +
                "WHERE cap.ComentarioId IS NULL AND cap.NovedadId IN (" + string.Join(",", ids) + ") " +
                "ORDER BY cap.NovedadId, cap.Orden, cap.Id" + SI_NO_VACIO);
        }

        public List<CapturaNovedadFila> LeerDeComentarios(IEnumerable<int> comentarios)
        {
            List<int> ids = (comentarios ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (ids.Count == 0)
            {
                return new List<CapturaNovedadFila>();
            }
            return Consultar(SI_EXISTE_LA_TABLA + "SELECT " + COLUMNAS + " FROM dbo.NovedadesCapturas cap " +
                "WHERE cap.ComentarioId IN (" + string.Join(",", ids) + ") " +
                "ORDER BY cap.ComentarioId, cap.Orden, cap.Id" + SI_NO_VACIO);
        }

        public CapturaNovedadFila Leer(int capturaId)
        {
            // Como GET .../Imagen: de una novedad publicada y, si es de un comentario, no borrado
            return Consultar(SI_EXISTE_LA_TABLA + "SELECT " + COLUMNAS + " FROM dbo.NovedadesCapturas cap " +
                "JOIN dbo.Novedades n ON n.Id = cap.NovedadId AND n.Publicada = 1 " +
                "LEFT JOIN dbo.NovedadesComentarios c ON c.Id = cap.ComentarioId " +
                "WHERE cap.Id = @id AND (cap.ComentarioId IS NULL OR c.Borrado = 0)" + SI_NO_VACIO,
                new SqlParameter("@id", capturaId)).FirstOrDefault();
        }

        public List<CapturaNovedadFila> Crear(int novedadId, int? comentarioId, IReadOnlyList<CapturaSubida> capturas, string usuario)
        {
            var creadas = new List<CapturaNovedadFila>();
            using (var db = new NVEntities())
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                foreach (CapturaSubida captura in capturas)
                {
                    creadas.Add(Insertar(db, novedadId, comentarioId, captura, usuario));
                }
                transaccion.Commit();
            }
            return creadas;
        }

        private static CapturaNovedadFila Insertar(NVEntities db, int novedadId, int? comentarioId, CapturaSubida captura, string usuario)
        {
            // UPDLOCK + HOLDLOCK: dos subidas a la vez al mismo sitio no repiten Orden
            return db.Database.SqlQuery<CapturaNovedadFila>(@"
                INSERT INTO dbo.NovedadesCapturas (NovedadId, ComentarioId, Ruta, Tipo, Bytes, Orden, Usuario)
                OUTPUT INSERTED.Id, INSERTED.NovedadId, INSERTED.ComentarioId, INSERTED.Ruta, INSERTED.Tipo, INSERTED.Bytes, INSERTED.Orden
                SELECT @novedad, @comentario, @ruta, @tipo, @bytes, ISNULL(MAX(Orden), 0) + 1, @usuario
                FROM dbo.NovedadesCapturas WITH (UPDLOCK, HOLDLOCK)
                WHERE NovedadId = @novedad AND ((@comentario IS NULL AND ComentarioId IS NULL) OR ComentarioId = @comentario);",
                new SqlParameter("@novedad", novedadId),
                new SqlParameter("@comentario", SqlDbType.Int) { Value = (object)comentarioId ?? DBNull.Value },
                new SqlParameter("@ruta", SqlDbType.VarChar, 400) { Value = captura.Ruta },
                new SqlParameter("@tipo", SqlDbType.VarChar, 30) { Value = captura.Tipo },
                new SqlParameter("@bytes", captura.Bytes),
                new SqlParameter("@usuario", SqlDbType.NVarChar, 128) { Value = (object)Recortar(usuario, 128) ?? DBNull.Value })
                .Single();
        }

        public ResumenImagenesPendientes ContarPendientes()
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<ResumenImagenesPendientes>(@"
                    SELECT (SELECT COUNT(*) FROM dbo.Novedades WHERE Imagen IS NOT NULL) AS Novedades,
                           (SELECT COUNT(*) FROM dbo.NovedadesComentarios WHERE Imagen IS NOT NULL) AS Comentarios,
                           CAST(ISNULL((SELECT SUM(CAST(DATALENGTH(Imagen) AS bigint)) FROM dbo.Novedades WHERE Imagen IS NOT NULL), 0)
                              + ISNULL((SELECT SUM(CAST(DATALENGTH(Imagen) AS bigint)) FROM dbo.NovedadesComentarios WHERE Imagen IS NOT NULL), 0) AS bigint) AS Bytes")
                    .Single();
            }
        }

        public List<ImagenPendienteMigrar> LeerPendientes(int maximo)
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<ImagenPendienteMigrar>(@"
                    SELECT TOP (@maximo) Origen, Id, NovedadId, Usuario, Imagen, ImagenTipo FROM (
                        SELECT CAST('Novedad' AS varchar(20)) AS Origen, Id, Id AS NovedadId,
                               CAST(SugeridaPor AS nvarchar(128)) AS Usuario, Imagen, CAST(ImagenTipo AS varchar(30)) AS ImagenTipo
                        FROM dbo.Novedades WHERE Imagen IS NOT NULL
                        UNION ALL
                        SELECT CAST('Comentario' AS varchar(20)), Id, NovedadId,
                               CAST(Usuario AS nvarchar(128)), Imagen, CAST(ImagenTipo AS varchar(30))
                        FROM dbo.NovedadesComentarios WHERE Imagen IS NOT NULL
                    ) p
                    ORDER BY Origen DESC, Id",
                    new SqlParameter("@maximo", maximo)).ToList();
            }
        }

        public bool RegistrarMigrada(ImagenPendienteMigrar imagen, CapturaSubida subida)
        {
            bool esComentario = imagen.Origen == ImagenPendienteMigrar.ORIGEN_COMENTARIO;
            string tabla = esComentario ? "dbo.NovedadesComentarios" : "dbo.Novedades";
            using (var db = new NVEntities())
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                // Se bloquea la fila; si ya no tiene imagen, otra pasada se adelantó
                bool sigue = db.Database.SqlQuery<int>(
                    "SELECT COUNT(*) FROM " + tabla + " WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id AND Imagen IS NOT NULL",
                    new SqlParameter("@id", imagen.Id)).Single() > 0;
                if (!sigue)
                {
                    transaccion.Rollback();
                    return false;
                }
                bool yaRegistrada = db.Database.SqlQuery<int>(
                    "SELECT COUNT(*) FROM dbo.NovedadesCapturas WHERE Ruta = @ruta",
                    new SqlParameter("@ruta", SqlDbType.VarChar, 400) { Value = subida.Ruta }).Single() > 0;
                if (!yaRegistrada)
                {
                    _ = Insertar(db, imagen.NovedadId, esComentario ? imagen.Id : (int?)null, subida, imagen.Usuario);
                }
                // ImagenTipo se deja: no estorba y dice lo que había
                _ = db.Database.ExecuteSqlCommand("UPDATE " + tabla + " SET Imagen = NULL WHERE Id = @id",
                    new SqlParameter("@id", imagen.Id));
                transaccion.Commit();
                return true;
            }
        }

        private static List<CapturaNovedadFila> Consultar(string sql, params object[] parametros)
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<CapturaNovedadFila>(sql, parametros).ToList();
            }
        }

        private static string Recortar(string texto, int longitud)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            string limpio = texto.Trim();
            return limpio.Length <= longitud ? limpio : limpio.Substring(0, longitud);
        }
    }
}
