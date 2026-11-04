using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Festivos
{
    /// <summary>NestoAPI#779: una fila de la tabla Festivos.</summary>
    public class FilaTablaFestivo
    {
        public DateTime Fecha { get; set; }
        public string Tipo { get; set; }
        public string Descripcion { get; set; }
        public string Usuario { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }

    /// <summary>NestoAPI#779: las filas de un año, o HayTabla = false si todavía no se ha lanzado el script.</summary>
    public class LecturaFestivosAnio
    {
        public bool HayTabla { get; set; }
        public List<FilaTablaFestivo> Filas { get; set; } = new List<FilaTablaFestivo>();
    }

    /// <summary>NestoAPI#779: lo que ha pasado al escribir (los valores los devuelve el propio SQL).</summary>
    public enum ResultadoEscrituraFestivo
    {
        SinTabla = 0,
        Creado = 1,
        Modificado = 2,
        Borrado = 3,
        NoExiste = 4
    }

    /// <summary>NestoAPI#779: acceso a <c>dbo.Festivos</c> (sustituible en los tests).</summary>
    public interface IRepositorioFestivos
    {
        Task<LecturaFestivosAnio> Leer(int anio);
        /// <summary>Alta o cambio de la descripción (la clave es Fecha + Tipo). Deja la fila en Modificaciones.</summary>
        Task<ResultadoEscrituraFestivo> Guardar(FilaTablaFestivo festivo, string usuario);
        /// <summary>Baja. Deja la fila en Modificaciones.</summary>
        Task<ResultadoEscrituraFestivo> Borrar(DateTime fecha, string tipo, string usuario);
    }

    public class RepositorioFestivos : IRepositorioFestivos
    {
        internal const string SQL_LEER_ANIO =
            "IF OBJECT_ID(N'dbo.Festivos', N'U') IS NULL " +
            "SELECT CAST(0 AS bit) AS HayTabla, CAST(NULL AS datetime) AS Fecha, CAST(NULL AS nvarchar(20)) AS Tipo, " +
            "CAST(NULL AS nvarchar(100)) AS Descripcion, CAST(NULL AS nvarchar(100)) AS Usuario, CAST(NULL AS datetime) AS FechaModificacion " +
            "ELSE SELECT CAST(1 AS bit) AS HayTabla, CAST(Fecha AS datetime) AS Fecha, RTRIM(Tipo) AS Tipo, Descripcion, Usuario, FechaModificacion " +
            "FROM dbo.Festivos WITH (NOLOCK) WHERE Fecha >= @desde AND Fecha < @hasta ORDER BY Fecha, Tipo";

        internal const string SQL_GUARDAR = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @resultado int = 0;
IF OBJECT_ID(N'dbo.Festivos', N'U') IS NOT NULL
BEGIN
    BEGIN TRANSACTION;
    DECLARE @anterior nvarchar(100) = (SELECT Descripcion FROM dbo.Festivos WITH (UPDLOCK, HOLDLOCK) WHERE Fecha = @fecha AND Tipo = @tipo);
    IF @anterior IS NULL
    BEGIN
        INSERT INTO dbo.Festivos (Fecha, Tipo, Descripcion, Usuario, FechaModificacion)
        VALUES (@fecha, @tipo, @descripcion, @usuario, GETDATE());
        SET @resultado = 1;
    END
    ELSE
    BEGIN
        UPDATE dbo.Festivos SET Descripcion = @descripcion, Usuario = @usuario, FechaModificacion = GETDATE()
        WHERE Fecha = @fecha AND Tipo = @tipo;
        SET @resultado = 2;
    END
    INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario)
    VALUES (N'Festivos',
            CONCAT(CONVERT(char(10), @fecha, 103), N' ', @tipo, N': ', ISNULL(@anterior, N'(no era festivo)')),
            CONCAT(CONVERT(char(10), @fecha, 103), N' ', @tipo, N': ', @descripcion),
            @usuario);
    COMMIT TRANSACTION;
END
SELECT @resultado;";

        internal const string SQL_BORRAR = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @resultado int = 0;
IF OBJECT_ID(N'dbo.Festivos', N'U') IS NOT NULL
BEGIN
    BEGIN TRANSACTION;
    DECLARE @anterior nvarchar(100) = (SELECT Descripcion FROM dbo.Festivos WITH (UPDLOCK, HOLDLOCK) WHERE Fecha = @fecha AND Tipo = @tipo);
    IF @anterior IS NULL
        SET @resultado = 4;
    ELSE
    BEGIN
        DELETE FROM dbo.Festivos WHERE Fecha = @fecha AND Tipo = @tipo;
        INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario)
        VALUES (N'Festivos',
                CONCAT(CONVERT(char(10), @fecha, 103), N' ', @tipo, N': ', @anterior),
                CONCAT(CONVERT(char(10), @fecha, 103), N' ', @tipo, N': (borrado)'),
                @usuario);
        SET @resultado = 3;
    END
    COMMIT TRANSACTION;
END
SELECT @resultado;";

        private readonly Func<NVEntities> crearContexto;

        public RepositorioFestivos(Func<NVEntities> crearContexto = null)
        {
            this.crearContexto = crearContexto ?? (() =>
            {
                var db = new NVEntities();
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return db;
            });
        }

        private class FilaLeida
        {
            public bool HayTabla { get; set; }
            public DateTime? Fecha { get; set; }
            public string Tipo { get; set; }
            public string Descripcion { get; set; }
            public string Usuario { get; set; }
            public DateTime? FechaModificacion { get; set; }
        }

        public async Task<LecturaFestivosAnio> Leer(int anio)
        {
            List<FilaLeida> filas;
            using (NVEntities db = crearContexto())
            {
                filas = await db.Database.SqlQuery<FilaLeida>(SQL_LEER_ANIO,
                    new SqlParameter("@desde", new DateTime(anio, 1, 1)),
                    new SqlParameter("@hasta", new DateTime(anio + 1, 1, 1))).ToListAsync().ConfigureAwait(false);
            }
            return new LecturaFestivosAnio
            {
                HayTabla = filas.Count == 0 || filas.Any(f => f.HayTabla),
                Filas = filas.Where(f => f.HayTabla && f.Fecha.HasValue).Select(f => new FilaTablaFestivo
                {
                    Fecha = f.Fecha.Value.Date,
                    Tipo = f.Tipo?.Trim(),
                    Descripcion = f.Descripcion,
                    Usuario = f.Usuario?.Trim(),
                    FechaModificacion = f.FechaModificacion
                }).ToList()
            };
        }

        public Task<ResultadoEscrituraFestivo> Guardar(FilaTablaFestivo festivo, string usuario) =>
            Ejecutar(SQL_GUARDAR, festivo.Fecha, festivo.Tipo, usuario, festivo.Descripcion);

        public Task<ResultadoEscrituraFestivo> Borrar(DateTime fecha, string tipo, string usuario) =>
            Ejecutar(SQL_BORRAR, fecha, tipo, usuario, null);

        private async Task<ResultadoEscrituraFestivo> Ejecutar(string sql, DateTime fecha, string tipo, string usuario, string descripcion)
        {
            var parametros = new List<SqlParameter>
            {
                new SqlParameter("@fecha", System.Data.SqlDbType.Date) { Value = fecha.Date },
                new SqlParameter("@tipo", tipo ?? string.Empty),
                new SqlParameter("@usuario", usuario ?? string.Empty)
            };
            if (descripcion != null)
            {
                parametros.Add(new SqlParameter("@descripcion", descripcion));
            }
            using (NVEntities db = crearContexto())
            {
                int resultado = await db.Database.SqlQuery<int>(sql, parametros.Cast<object>().ToArray())
                    .FirstAsync().ConfigureAwait(false);
                return (ResultadoEscrituraFestivo)resultado;
            }
        }
    }
}
