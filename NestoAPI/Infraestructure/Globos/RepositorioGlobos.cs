using NestoAPI.Models;
using NestoAPI.Models.Globos;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.Globos
{
    public enum ResultadoVistoGlobo
    {
        Marcado,
        YaEstaba,
        NoExiste,
        /// <summary>Aún no se ha lanzado Scripts/Globos.sql.</summary>
        FaltaScript
    }

    /// <summary>NestoAPI#774: las tablas Globos y GlobosVistos (fuera del EDMX; Scripts/Globos.sql).</summary>
    public interface IRepositorioGlobos
    {
        /// <summary>Los activos de la aplicación (y de la pantalla, si se pide). Vacía si falta el script.</summary>
        List<GloboFila> LeerActivos(string aplicacion, string pantalla);

        /// <summary>Los Id que ya ha visto el usuario de entre <paramref name="ids"/>.</summary>
        HashSet<int> LeerVistos(string usuario, IReadOnlyCollection<int> ids);

        /// <summary>Idempotente: la segunda vez devuelve <see cref="ResultadoVistoGlobo.YaEstaba"/>.</summary>
        ResultadoVistoGlobo MarcarVisto(string usuario, int globoId);
    }

    public class RepositorioGlobosSql : IRepositorioGlobos
    {
        internal const string SQL_LEER = @"
            IF OBJECT_ID('dbo.Globos', 'U') IS NOT NULL
                SELECT Id, Aplicacion, VersionDesde, Pantalla, Ancla, Titulo, Texto, Grupos, FechaDesde, FechaHasta, Orden, NovedadId, Activo
                FROM dbo.Globos
                WHERE Activo = 1 AND Aplicacion = @aplicacion AND (@pantalla IS NULL OR Pantalla = @pantalla)
                  AND FechaDesde <= CAST(GETDATE() AS date) AND (FechaHasta IS NULL OR FechaHasta >= CAST(GETDATE() AS date));";

        // Idempotente también con dos llamadas a la vez: la PK (Usuario, GloboId) para la segunda, que se da por «ya estaba».
        internal const string SQL_MARCAR = @"
            IF OBJECT_ID('dbo.GlobosVistos', 'U') IS NULL
                SELECT 3;
            ELSE IF NOT EXISTS (SELECT 1 FROM dbo.Globos WHERE Id = @id)
                SELECT 2;
            ELSE IF EXISTS (SELECT 1 FROM dbo.GlobosVistos WHERE Usuario = @usuario AND GloboId = @id)
                SELECT 1;
            ELSE
            BEGIN
                BEGIN TRY
                    INSERT INTO dbo.GlobosVistos (Usuario, GloboId) VALUES (@usuario, @id);
                    SELECT 0;
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() IN (2627, 2601) SELECT 1; ELSE THROW;
                END CATCH
            END";

        public List<GloboFila> LeerActivos(string aplicacion, string pantalla)
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<GloboFila>(SQL_LEER,
                    new SqlParameter("@aplicacion", SqlDbType.VarChar, 10) { Value = aplicacion },
                    new SqlParameter("@pantalla", SqlDbType.NVarChar, 60) { Value = string.IsNullOrWhiteSpace(pantalla) ? (object)System.DBNull.Value : pantalla.Trim() })
                    .ToList();
            }
        }

        public HashSet<int> LeerVistos(string usuario, IReadOnlyCollection<int> ids)
        {
            if (string.IsNullOrEmpty(usuario) || ids == null || ids.Count == 0)
            {
                return new HashSet<int>();
            }
            // Pocos Id (los de una pantalla): IN con los números, que son enteros (sin inyección posible)
            string lista = string.Join(",", ids.Distinct());
            using (var db = new NVEntities())
            {
                return new HashSet<int>(db.Database.SqlQuery<int>(
                    "IF OBJECT_ID('dbo.GlobosVistos', 'U') IS NOT NULL " +
                    "SELECT GloboId FROM dbo.GlobosVistos WHERE Usuario = @usuario AND GloboId IN (" + lista + ")",
                    new SqlParameter("@usuario", SqlDbType.NVarChar, ReglasGlobos.LONGITUD_USUARIO) { Value = usuario }).ToList());
            }
        }

        public ResultadoVistoGlobo MarcarVisto(string usuario, int globoId)
        {
            using (var db = new NVEntities())
            {
                int resultado = db.Database.SqlQuery<int>(SQL_MARCAR,
                    new SqlParameter("@usuario", SqlDbType.NVarChar, ReglasGlobos.LONGITUD_USUARIO) { Value = usuario },
                    new SqlParameter("@id", globoId)).Single();
                switch (resultado)
                {
                    case 0: return ResultadoVistoGlobo.Marcado;
                    case 1: return ResultadoVistoGlobo.YaEstaba;
                    case 2: return ResultadoVistoGlobo.NoExiste;
                    default: return ResultadoVistoGlobo.FaltaScript;
                }
            }
        }
    }
}
