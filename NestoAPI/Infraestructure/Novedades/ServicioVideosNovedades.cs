using NestoAPI.Models;
using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>NestoAPI#711: el vídeo de las novedades (la ruta del blob en Novedades.Video) y sus reproducciones.</summary>
    public interface IServicioVideosNovedades
    {
        /// <summary>Id → tamaño de las novedades que tienen vídeo. Vacío si la columna aún no existe.</summary>
        Dictionary<int, int?> LeerConVideo();

        /// <summary>La novedad con su vídeo (Video null si no tiene o si la columna aún no existe), o null si no existe.</summary>
        VideoNovedadFila Leer(int novedadId);

        /// <summary>Si ya existen las columnas (Scripts/Issue711_NovedadesVideo.sql).</summary>
        bool AdmiteVideos();

        /// <summary>
        /// Pone el vídeo a la novedad SOLO si ninguna otra de la misma versión y ámbito tiene ya uno, en la misma
        /// transacción (dos subidas a la vez no se cuelan). Sustituir el vídeo de la misma novedad sí se puede.
        /// </summary>
        ResultadoGuardarVideoNovedad GuardarVideo(int novedadId, string ruta, int tamano, string usuario);

        /// <summary>Le quita el vídeo. Devuelve la ruta del blob que tenía, o null si no tenía.</summary>
        string QuitarVideo(int novedadId, string usuario);

        /// <summary>Apunta quién lo ha visto y cuándo. No hace nada si la tabla aún no existe.</summary>
        void RegistrarReproduccion(int novedadId, string usuario, string cliente);
    }

    /// <summary>
    /// NestoAPI#711: SQL parametrizado como el resto de Novedades (fuera del EDMX). Las columnas Video y VideoTamano se
    /// leen y escriben con SQL dinámico (COL_LENGTH) para que la API se pueda publicar antes que el script: un SELECT
    /// con la columna no compilaría.
    /// </summary>
    public class ServicioVideosNovedades : IServicioVideosNovedades
    {
        private const string EXISTEN_COLUMNAS =
            "COL_LENGTH('dbo.Novedades', 'Video') IS NOT NULL AND COL_LENGTH('dbo.Novedades', 'VideoTamano') IS NOT NULL";

        public Dictionary<int, int?> LeerConVideo()
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<VideoNovedadResumenFila>(
                    "IF " + EXISTEN_COLUMNAS + " " +
                    "EXEC sp_executesql N'SELECT Id, VideoTamano FROM dbo.Novedades WHERE Video IS NOT NULL' " +
                    "ELSE SELECT CAST(0 AS int) AS Id, CAST(NULL AS int) AS VideoTamano WHERE 1 = 0")
                    .ToList()
                    .GroupBy(f => f.Id)
                    .ToDictionary(g => g.Key, g => g.First().VideoTamano);
            }
        }

        public VideoNovedadFila Leer(int novedadId)
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<VideoNovedadFila>(
                    "IF " + EXISTEN_COLUMNAS + " " +
                    "EXEC sp_executesql N'SELECT Id, Version, Ambito, Titulo, Publicada, Video, VideoTamano FROM dbo.Novedades WHERE Id = @id', " +
                    "N'@id int', @id " +
                    "ELSE SELECT Id, Version, Ambito, Titulo, Publicada, CAST(NULL AS nvarchar(400)) AS Video, CAST(NULL AS int) AS VideoTamano " +
                    "FROM dbo.Novedades WHERE Id = @id",
                    new SqlParameter("@id", novedadId)).FirstOrDefault();
            }
        }

        public bool AdmiteVideos()
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<bool>(
                    "SELECT CAST(CASE WHEN " + EXISTEN_COLUMNAS + " THEN 1 ELSE 0 END AS bit)").Single();
            }
        }

        public ResultadoGuardarVideoNovedad GuardarVideo(int novedadId, string ruta, int tamano, string usuario)
        {
            // UPDLOCK + HOLDLOCK sobre la búsqueda de «otra con vídeo»: dos subidas a la vez a novedades distintas de la
            // misma versión no pueden acabar las dos con vídeo. La tabla tiene unos cientos de filas.
            const string SQL = @"
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @version varchar(23), @ambito nvarchar(20), @anterior nvarchar(400), @otra int, @otroTitulo nvarchar(200);
SELECT @version = Version, @ambito = Ambito, @anterior = Video FROM dbo.Novedades WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id;
SELECT TOP (1) @otra = Id, @otroTitulo = Titulo FROM dbo.Novedades WITH (UPDLOCK, HOLDLOCK)
WHERE Version = @version AND Ambito = @ambito AND Id <> @id AND Video IS NOT NULL
ORDER BY Id;
IF @otra IS NULL
    UPDATE dbo.Novedades SET Video = @ruta, VideoTamano = @tamano, Usuario = @usuario, Fecha_Modificación = GETDATE() WHERE Id = @id;
COMMIT;
SELECT CAST(CASE WHEN @otra IS NULL THEN 1 ELSE 0 END AS bit) AS Guardado, @anterior AS RutaAnterior,
       @otra AS OtraNovedadId, @otroTitulo AS OtroTitulo, @version AS Version;";
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<ResultadoGuardarVideoNovedad>(
                    "IF " + EXISTEN_COLUMNAS + " " +
                    "EXEC sp_executesql N'" + SQL.Replace("'", "''") + "', " +
                    "N'@id int, @ruta nvarchar(400), @tamano int, @usuario nvarchar(50)', @id, @ruta, @tamano, @usuario " +
                    "ELSE SELECT CAST(0 AS bit) AS Guardado, CAST(NULL AS nvarchar(400)) AS RutaAnterior, CAST(NULL AS int) AS OtraNovedadId, " +
                    "CAST(NULL AS nvarchar(200)) AS OtroTitulo, CAST(NULL AS varchar(23)) AS Version WHERE 1 = 0",
                    new SqlParameter("@id", novedadId),
                    new SqlParameter("@ruta", SqlDbType.NVarChar, 400) { Value = ruta },
                    new SqlParameter("@tamano", tamano),
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = Recortar(usuario, 50) })
                    .FirstOrDefault();
            }
        }

        public string QuitarVideo(int novedadId, string usuario)
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<string>(
                    "IF " + EXISTEN_COLUMNAS + " " +
                    "EXEC sp_executesql N'UPDATE dbo.Novedades SET Video = NULL, VideoTamano = NULL, Usuario = @usuario, Fecha_Modificación = GETDATE() " +
                    "OUTPUT DELETED.Video WHERE Id = @id AND Video IS NOT NULL', N'@id int, @usuario nvarchar(50)', @id, @usuario " +
                    "ELSE SELECT CAST(NULL AS nvarchar(400)) WHERE 1 = 0",
                    new SqlParameter("@id", novedadId),
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = Recortar(usuario, 50) })
                    .FirstOrDefault();
            }
        }

        public void RegistrarReproduccion(int novedadId, string usuario, string cliente)
        {
            using (var db = new NVEntities())
            {
                db.Database.ExecuteSqlCommand(
                    "IF OBJECT_ID(N'dbo.NovedadesVideoReproducciones', N'U') IS NOT NULL " +
                    "INSERT INTO dbo.NovedadesVideoReproducciones (NovedadId, Usuario, Cliente) VALUES (@id, @usuario, @cliente)",
                    new SqlParameter("@id", novedadId),
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 128) { Value = Recortar(usuario, 128) },
                    new SqlParameter("@cliente", SqlDbType.NVarChar, 20) { Value = string.IsNullOrWhiteSpace(cliente) ? (object)DBNull.Value : Recortar(cliente, 20) });
            }
        }

        private static string Recortar(string texto, int longitud)
        {
            string limpio = (texto ?? string.Empty).Trim();
            return limpio.Length <= longitud ? limpio : limpio.Substring(0, longitud);
        }
    }
}
