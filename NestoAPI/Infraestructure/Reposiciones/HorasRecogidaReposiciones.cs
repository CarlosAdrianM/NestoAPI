using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>
    /// Sugerencia 581 (Carlos, 13/10/26): lo que hay que saber antes de una reposición puntual: hasta qué hora se puede hacer
    /// hoy (la del repartidor) y si ya ha pasado.
    /// </summary>
    public class LimiteReposicionPuntualDTO
    {
        public string Origen { get; set; }
        public string Destino { get; set; }
        /// <summary>A qué hora pasa hoy el repartidor por <see cref="Origen"/> (ReposicionesCalendario.HoraRecogida). Null: sin límite.</summary>
        public TimeSpan? HoraRecogida { get; set; }
        /// <summary>Ya ha pasado: la puntual no sale hoy y la API la rechaza (409).</summary>
        public bool YaHaPasado { get; set; }
        /// <summary>El motivo, para enseñarlo tal cual. Null si se puede hacer.</summary>
        public string Mensaje { get; set; }
    }

    /// <summary>
    /// Sugerencia 581: la columna ReposicionesCalendario.HoraRecogida (time NULL; NULL = sin límite), a qué hora pasa el
    /// repartidor a recoger la reposición en el origen. NO está en el EDMX: se lee y se escribe con SQL y, mientras no se lance
    /// Scripts/Issue581_ReposicionesCalendario_HoraRecogida.sql, no existe y todo se comporta como «sin límite» (nada falla).
    /// </summary>
    public interface IHorasRecogidaReposiciones
    {
        /// <summary>Si la columna ya existe (se ha lanzado el script).</summary>
        Task<bool> HayColumna();
        /// <summary>Id de la fila → su hora de recogida (null = sin límite). Vacío sin la columna.</summary>
        Task<Dictionary<int, TimeSpan?>> Leer(string empresa);
        /// <summary>Pone la hora de recogida de cada fila (por Id). No hace nada sin la columna.</summary>
        Task Guardar(IReadOnlyDictionary<int, TimeSpan?> horas);
        /// <summary>
        /// La hora de recogida del viaje de hoy de la ruta: la fila activa de ese día de la semana (1 lunes … 7 domingo). Null
        /// si no hay viaje hoy, no tiene hora o no hay columna.
        /// </summary>
        Task<TimeSpan?> DelDia(string empresa, string origen, string destino, byte diaSemana);
    }

    /// <summary>Sin columna (pruebas que no la miran): todo sin límite.</summary>
    public sealed class SinHorasRecogidaReposiciones : IHorasRecogidaReposiciones
    {
        public static readonly SinHorasRecogidaReposiciones Instancia = new SinHorasRecogidaReposiciones();

        private SinHorasRecogidaReposiciones()
        {
        }

        public Task<bool> HayColumna() => Task.FromResult(false);
        public Task<Dictionary<int, TimeSpan?>> Leer(string empresa) => Task.FromResult(new Dictionary<int, TimeSpan?>());
        public Task Guardar(IReadOnlyDictionary<int, TimeSpan?> horas) => Task.CompletedTask;
        public Task<TimeSpan?> DelDia(string empresa, string origen, string destino, byte diaSemana) => Task.FromResult<TimeSpan?>(null);
    }

    public class HorasRecogidaReposicionesSql : IHorasRecogidaReposiciones
    {
        internal const string SQL_HAY_COLUMNA = @"
SELECT CAST(CASE WHEN COL_LENGTH('dbo.ReposicionesCalendario', 'HoraRecogida') IS NULL THEN 0 ELSE 1 END AS bit)";

        // La columna puede no existir todavía: una consulta que la nombra fallaría al compilarse aunque esté dentro del IF,
        // así que va por sp_executesql (solo se compila si existe).
        internal const string SQL_LEER = @"
IF COL_LENGTH('dbo.ReposicionesCalendario', 'HoraRecogida') IS NULL
    SELECT CAST(NULL AS int) AS Id, CAST(NULL AS time(0)) AS HoraRecogida WHERE 1 = 0
ELSE
    EXEC sp_executesql N'SELECT Id, HoraRecogida FROM dbo.ReposicionesCalendario WHERE Empresa = @empresa',
        N'@empresa char(3)', @empresa = @p0";

        internal const string SQL_GUARDAR = @"
IF COL_LENGTH('dbo.ReposicionesCalendario', 'HoraRecogida') IS NOT NULL
    EXEC sp_executesql N'UPDATE dbo.ReposicionesCalendario SET HoraRecogida = @hora WHERE Id = @id',
        N'@id int, @hora time(0)', @id = @p0, @hora = @p1";

        internal const string SQL_DEL_DIA = @"
IF COL_LENGTH('dbo.ReposicionesCalendario', 'HoraRecogida') IS NULL
    SELECT CAST(NULL AS int) AS Id, CAST(NULL AS time(0)) AS HoraRecogida WHERE 1 = 0
ELSE
    EXEC sp_executesql N'SELECT TOP 1 Id, HoraRecogida FROM dbo.ReposicionesCalendario
        WHERE Empresa = @empresa AND AlmacenOrigen = @origen AND AlmacenDestino = @destino AND DiaSemana = @dia AND Activo = 1
          AND HoraRecogida IS NOT NULL
        ORDER BY HoraRecogida',
        N'@empresa char(3), @origen char(3), @destino char(3), @dia tinyint',
        @empresa = @p0, @origen = @p1, @destino = @p2, @dia = @p3";

        private readonly NVEntities db;

        public HorasRecogidaReposicionesSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public Task<bool> HayColumna()
        {
            return db.Database.SqlQuery<bool>(SQL_HAY_COLUMNA).SingleAsync();
        }

        public async Task<Dictionary<int, TimeSpan?>> Leer(string empresa)
        {
            List<FilaHoraRecogida> filas = await db.Database.SqlQuery<FilaHoraRecogida>(SQL_LEER,
                    new SqlParameter("@p0", SqlDbType.Char, 3) { Value = empresa })
                .ToListAsync().ConfigureAwait(false);
            return filas.Where(f => f.Id.HasValue).ToDictionary(f => f.Id.Value, f => f.HoraRecogida);
        }

        public async Task Guardar(IReadOnlyDictionary<int, TimeSpan?> horas)
        {
            foreach (KeyValuePair<int, TimeSpan?> hora in horas ?? new Dictionary<int, TimeSpan?>())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_GUARDAR,
                    new SqlParameter("@p0", SqlDbType.Int) { Value = hora.Key },
                    new SqlParameter("@p1", SqlDbType.Time) { Value = hora.Value.HasValue ? (object)hora.Value.Value : DBNull.Value })
                    .ConfigureAwait(false);
            }
        }

        public async Task<TimeSpan?> DelDia(string empresa, string origen, string destino, byte diaSemana)
        {
            FilaHoraRecogida fila = await db.Database.SqlQuery<FilaHoraRecogida>(SQL_DEL_DIA,
                    new SqlParameter("@p0", SqlDbType.Char, 3) { Value = empresa },
                    new SqlParameter("@p1", SqlDbType.Char, 3) { Value = origen },
                    new SqlParameter("@p2", SqlDbType.Char, 3) { Value = destino },
                    new SqlParameter("@p3", SqlDbType.TinyInt) { Value = diaSemana })
                .FirstOrDefaultAsync().ConfigureAwait(false);
            return fila?.HoraRecogida;
        }

        private class FilaHoraRecogida
        {
            public int? Id { get; set; }
            public TimeSpan? HoraRecogida { get; set; }
        }
    }

    /// <summary>
    /// Sugerencia 581 (Carlos, 13/10/26: «hasta la recogida»): la reposición puntual de una tienda a Algete solo se puede hacer
    /// hasta la hora a la que pasa hoy el repartidor por la tienda (la del viaje de hoy en el calendario). Después no sale hoy:
    /// se rechaza (no se deja para el día siguiente). Sin viaje hoy o sin hora puesta: sin límite.
    /// </summary>
    public static class LimiteRecogidaPuntual
    {
        /// <summary>1 lunes … 7 domingo, como ReposicionesCalendario.DiaSemana.</summary>
        public static byte DiaSemana(DateTime dia) => (byte)(((int)dia.DayOfWeek + 6) % 7 + 1);

        public static LimiteReposicionPuntualDTO Calcular(string origen, string destino, TimeSpan? horaRecogida, DateTime ahora)
        {
            bool yaHaPasado = horaRecogida.HasValue && ahora.TimeOfDay >= horaRecogida.Value;
            return new LimiteReposicionPuntualDTO
            {
                Origen = origen,
                Destino = destino,
                HoraRecogida = horaRecogida,
                YaHaPasado = yaHaPasado,
                Mensaje = yaHaPasado ? Mensaje(horaRecogida.Value) : null
            };
        }

        public static string Mensaje(TimeSpan horaRecogida) =>
            $"Ya ha pasado el repartidor de hoy ({horaRecogida:hh\\:mm}): la reposición puntual ya no sale hoy.";
    }
}
