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
    /// <summary>NestoAPI#616: los PDF e imágenes colgados en las novedades.</summary>
    public interface IServicioAdjuntosNovedades
    {
        /// <summary>
        /// Los adjuntos de todas esas novedades en UNA consulta y sin el contenido. Lista vacía si la
        /// tabla aún no existe.
        /// </summary>
        List<AdjuntoNovedadFila> LeerResumen(IEnumerable<int> novedades);

        /// <summary>El fichero, o null si no existe.</summary>
        AdjuntoNovedadContenido Leer(int adjuntoId);

        bool ExisteNovedad(int novedadId);

        /// <summary>Graba todos o ninguno, cada uno con el Orden siguiente al último de la novedad.</summary>
        List<AdjuntoNovedadDTO> Crear(int novedadId, IReadOnlyList<AdjuntoNovedadAGrabar> adjuntos, string usuario);

        /// <summary>False si no existía.</summary>
        bool Borrar(int adjuntoId);
    }

    /// <summary>
    /// NestoAPI#616: SQL parametrizado sobre NovedadesAdjuntos (Scripts/Issue616_NovedadesAdjuntos.sql),
    /// fuera del EDMX como el resto de tablas de Novedades. Las lecturas comprueban antes que la tabla
    /// exista, para que la API se pueda publicar antes que el script sin que nada falle.
    /// </summary>
    public class ServicioAdjuntosNovedades : IServicioAdjuntosNovedades
    {
        private const string SI_EXISTE_LA_TABLA = "IF OBJECT_ID(N'dbo.NovedadesAdjuntos', N'U') IS NOT NULL ";

        public List<AdjuntoNovedadFila> LeerResumen(IEnumerable<int> novedades)
        {
            List<int> ids = (novedades ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (ids.Count == 0)
            {
                return new List<AdjuntoNovedadFila>();
            }
            // Los ids son int: se pueden componer en el IN sin riesgo de inyección.
            string sql = SI_EXISTE_LA_TABLA +
                "SELECT NovedadId, Id, Nombre, Tipo, Tamano, Orden FROM dbo.NovedadesAdjuntos " +
                "WHERE NovedadId IN (" + string.Join(",", ids) + ") ORDER BY NovedadId, Orden, Id " +
                // Sin tabla, el mismo resultado vacío (con columnas, para que SqlQuery lo materialice)
                "ELSE SELECT CAST(0 AS int) AS NovedadId, CAST(0 AS int) AS Id, CAST(NULL AS nvarchar(200)) AS Nombre, " +
                "CAST(NULL AS varchar(100)) AS Tipo, CAST(0 AS int) AS Tamano, CAST(0 AS int) AS Orden WHERE 1 = 0";
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<AdjuntoNovedadFila>(sql).ToList();
            }
        }

        public AdjuntoNovedadContenido Leer(int adjuntoId)
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<AdjuntoNovedadContenido>(SI_EXISTE_LA_TABLA +
                    "SELECT Nombre, Tipo, Contenido FROM dbo.NovedadesAdjuntos WHERE Id = @id " +
                    "ELSE SELECT CAST(NULL AS nvarchar(200)) AS Nombre, CAST(NULL AS varchar(100)) AS Tipo, " +
                    "CAST(NULL AS varbinary(max)) AS Contenido WHERE 1 = 0",
                    new SqlParameter("@id", adjuntoId)).FirstOrDefault();
            }
        }

        public bool ExisteNovedad(int novedadId)
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<int>("SELECT COUNT(*) FROM Novedades WHERE Id = @id",
                    new SqlParameter("@id", novedadId)).Single() > 0;
            }
        }

        public List<AdjuntoNovedadDTO> Crear(int novedadId, IReadOnlyList<AdjuntoNovedadAGrabar> adjuntos, string usuario)
        {
            var creados = new List<AdjuntoNovedadDTO>();
            using (var db = new NVEntities())
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                foreach (AdjuntoNovedadAGrabar adjunto in adjuntos)
                {
                    // UPDLOCK + HOLDLOCK: dos subidas a la vez a la misma novedad no repiten Orden
                    AdjuntoNovedadFila fila = db.Database.SqlQuery<AdjuntoNovedadFila>(@"
                        INSERT INTO dbo.NovedadesAdjuntos (NovedadId, Nombre, Tipo, Tamano, Contenido, Orden, Usuario)
                        OUTPUT INSERTED.NovedadId, INSERTED.Id, INSERTED.Nombre, INSERTED.Tipo, INSERTED.Tamano, INSERTED.Orden
                        SELECT @novedad, @nombre, @tipo, @tamano, @contenido, ISNULL(MAX(Orden), 0) + 1, @usuario
                        FROM dbo.NovedadesAdjuntos WITH (UPDLOCK, HOLDLOCK)
                        WHERE NovedadId = @novedad;",
                        new SqlParameter("@novedad", novedadId),
                        new SqlParameter("@nombre", SqlDbType.NVarChar, 200) { Value = adjunto.Nombre },
                        new SqlParameter("@tipo", SqlDbType.VarChar, 100) { Value = adjunto.Tipo },
                        new SqlParameter("@tamano", adjunto.Contenido.Length),
                        new SqlParameter("@contenido", SqlDbType.VarBinary, -1) { Value = adjunto.Contenido },
                        new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = (object)Recortar(usuario, 50) ?? DBNull.Value })
                        .Single();
                    creados.Add(fila.ADto());
                }
                transaccion.Commit();
            }
            return creados;
        }

        public bool Borrar(int adjuntoId)
        {
            using (var db = new NVEntities())
            {
                return db.Database.ExecuteSqlCommand("DELETE FROM dbo.NovedadesAdjuntos WHERE Id = @id",
                    new SqlParameter("@id", adjuntoId)) > 0;
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
