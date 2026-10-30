using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#759: una fila de RepartoParadasReposicion (una parada «Reposición {tienda}» de un día).</summary>
    public class ParadaReposicion
    {
        public const string ESTADO_EN_REPARTO = "EnReparto";
        public const string ESTADO_ENTREGADA = "Entregada";

        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        /// <summary>El almacén destino: REI o ALC.</summary>
        public string Destino { get; set; }
        public string Estado { get; set; }
        /// <summary>Quién la creó (el primero que cargó un bulto).</summary>
        public string Usuario { get; set; }
        public DateTime FechaRegistro { get; set; }
        public DateTime? FechaEntrega { get; set; }
        public string UsuarioEntrega { get; set; }
        public Guid? ClaveEntrega { get; set; }
        public string Receptor { get; set; }

        public bool Entregada => Estado == ESTADO_ENTREGADA;
    }

    /// <summary>NestoAPI#759: una fila de RepartoParadasReposicionBultos: un bulto R{traspaso}-{N} cargado en una parada de reposición.</summary>
    public class CargaBultoParadaReposicion
    {
        public int Id { get; set; }
        public int ParadaId { get; set; }
        public string Empresa { get; set; }
        public int Traspaso { get; set; }
        public int Bulto { get; set; }
        /// <summary>Los bultos del traspaso (ReposicionesBultos.Total) cuando se cargó.</summary>
        public int Total { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaHora { get; set; }
        public Guid ClaveIdempotencia { get; set; }
        public DateTime FechaRegistro { get; set; }
    }

    public interface IRepositorioParadasReposicion
    {
        /// <summary>¿Existen las tablas de Scripts/Issue759_ParadasReparto.sql?</summary>
        bool ScriptAplicado();
        /// <summary>Las paradas sin entregar con Fecha en [desde, hasta).</summary>
        Task<List<ParadaReposicion>> LeerEnReparto(DateTime desde, DateTime hasta);
        Task<List<ParadaReposicion>> LeerParadas(IReadOnlyCollection<int> ids);
        /// <summary>Las cargas de esas paradas, por Id.</summary>
        Task<List<CargaBultoParadaReposicion>> LeerCargas(IReadOnlyCollection<int> paradas);
        /// <summary>La última carga de ese bulto (en cualquier parada), o null.</summary>
        Task<CargaBultoParadaReposicion> LeerCargaDelBulto(string empresa, int traspaso, int bulto);
        Task<CargaBultoParadaReposicion> LeerCargaPorClave(Guid clave);
        /// <summary>La parada sin entregar de ese día y esa tienda, o null.</summary>
        Task<ParadaReposicion> LeerAbierta(DateTime fecha, string destino);
        /// <summary>
        /// En UNA transacción: si <c>carga.ParadaId</c> es 0, la parada sin entregar de <paramref name="fecha"/> y
        /// <paramref name="destino"/> (con bloqueo) o una nueva; y la carga. Devuelve el Id de la parada.
        /// </summary>
        /// <exception cref="ClaveIdempotenciaRepetidaException">Otra petición ya registró esa clave.</exception>
        Task<int> Cargar(CargaBultoParadaReposicion carga, DateTime fecha, string destino);
        /// <summary>La da por entregada si sigue en reparto. False si ya no lo estaba.</summary>
        Task<bool> Entregar(int parada, Guid clave, DateTime fechaHora, string usuario, string receptor, string observaciones, decimal? latitud, decimal? longitud);
    }

    /// <summary>NestoAPI#759: las tablas de las paradas de reposición, fuera del EDMX (SQL a mano).</summary>
    public class RepositorioParadasReposicion : IRepositorioParadasReposicion
    {
        public const string TABLA = "RepartoParadasReposicion";
        public const string TABLA_BULTOS = "RepartoParadasReposicionBultos";

        private const string COLUMNAS = "Id, CAST(Fecha AS datetime) AS Fecha, RTRIM(Destino) AS Destino, RTRIM(Estado) AS Estado, RTRIM(Usuario) AS Usuario, " +
            "FechaRegistro, FechaEntrega, RTRIM(UsuarioEntrega) AS UsuarioEntrega, ClaveEntrega, Receptor";
        private const string COLUMNAS_BULTOS = "Id, ParadaId, RTRIM(Empresa) AS Empresa, NumTraspaso AS Traspaso, CAST(Bulto AS int) AS Bulto, " +
            "CAST(Total AS int) AS Total, RTRIM(Usuario) AS Usuario, FechaHora, ClaveIdempotencia, FechaRegistro";

        private const string SQL_PARADA_ABIERTA_O_NUEVA = @"
SET NOCOUNT ON;
DECLARE @Id int;
SELECT TOP 1 @Id = Id FROM dbo." + TABLA + @" WITH (UPDLOCK, HOLDLOCK)
WHERE Fecha = @Fecha AND Destino = @Destino AND Estado = '" + ParadaReposicion.ESTADO_EN_REPARTO + @"' ORDER BY Id;
IF @Id IS NULL
BEGIN
    INSERT INTO dbo." + TABLA + @" (Fecha, Destino, Estado, Usuario, FechaRegistro)
    VALUES (@Fecha, @Destino, '" + ParadaReposicion.ESTADO_EN_REPARTO + @"', @Usuario, GETDATE());
    SET @Id = CAST(SCOPE_IDENTITY() AS int);
END
SELECT @Id;";

        private readonly NVEntities db;

        public RepositorioParadasReposicion(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public bool ScriptAplicado() => ColumnasOpcionales.ExisteTabla(db, TABLA) && ColumnasOpcionales.ExisteTabla(db, TABLA_BULTOS);

        public Task<List<ParadaReposicion>> LeerEnReparto(DateTime desde, DateTime hasta)
            => db.Database.SqlQuery<ParadaReposicion>(
                $"SELECT {COLUMNAS} FROM dbo.{TABLA} WHERE Estado = @p0 AND Fecha >= @p1 AND Fecha < @p2 ORDER BY Id",
                ParadaReposicion.ESTADO_EN_REPARTO, desde, hasta).ToListAsync();

        public Task<List<ParadaReposicion>> LeerParadas(IReadOnlyCollection<int> ids)
        {
            string lista = Lista(ids);
            return lista == null
                ? Task.FromResult(new List<ParadaReposicion>())
                : db.Database.SqlQuery<ParadaReposicion>($"SELECT {COLUMNAS} FROM dbo.{TABLA} WHERE Id IN ({lista})").ToListAsync();
        }

        public Task<List<CargaBultoParadaReposicion>> LeerCargas(IReadOnlyCollection<int> paradas)
        {
            string lista = Lista(paradas);
            return lista == null
                ? Task.FromResult(new List<CargaBultoParadaReposicion>())
                : db.Database.SqlQuery<CargaBultoParadaReposicion>($"SELECT {COLUMNAS_BULTOS} FROM dbo.{TABLA_BULTOS} WHERE ParadaId IN ({lista}) ORDER BY Id").ToListAsync();
        }

        public async Task<CargaBultoParadaReposicion> LeerCargaDelBulto(string empresa, int traspaso, int bulto)
            => (await db.Database.SqlQuery<CargaBultoParadaReposicion>(
                $"SELECT TOP 1 {COLUMNAS_BULTOS} FROM dbo.{TABLA_BULTOS} WHERE Empresa = @p0 AND NumTraspaso = @p1 AND Bulto = @p2 ORDER BY Id DESC",
                empresa, traspaso, bulto).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<CargaBultoParadaReposicion> LeerCargaPorClave(Guid clave)
            => (await db.Database.SqlQuery<CargaBultoParadaReposicion>(
                $"SELECT TOP 1 {COLUMNAS_BULTOS} FROM dbo.{TABLA_BULTOS} WHERE ClaveIdempotencia = @clave",
                new SqlParameter("@clave", SqlDbType.UniqueIdentifier) { Value = clave }).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<ParadaReposicion> LeerAbierta(DateTime fecha, string destino)
            => (await db.Database.SqlQuery<ParadaReposicion>(
                $"SELECT TOP 1 {COLUMNAS} FROM dbo.{TABLA} WHERE Fecha = @fecha AND Destino = @destino AND Estado = @estado ORDER BY Id",
                new SqlParameter("@fecha", SqlDbType.Date) { Value = fecha.Date },
                new SqlParameter("@destino", SqlDbType.Char, 3) { Value = destino ?? string.Empty },
                new SqlParameter("@estado", SqlDbType.VarChar, 12) { Value = ParadaReposicion.ESTADO_EN_REPARTO }).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<int> Cargar(CargaBultoParadaReposicion carga, DateTime fecha, string destino)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    int parada = carga.ParadaId;
                    if (parada <= 0)
                    {
                        parada = (await db.Database.SqlQuery<int>(SQL_PARADA_ABIERTA_O_NUEVA,
                            new SqlParameter("@Fecha", SqlDbType.Date) { Value = fecha.Date },
                            new SqlParameter("@Destino", SqlDbType.Char, 3) { Value = destino ?? string.Empty },
                            new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(carga.Usuario, 50) ?? string.Empty })
                            .ToListAsync().ConfigureAwait(false)).First();
                    }
                    try
                    {
                        _ = await db.Database.ExecuteSqlCommandAsync(
                            $"INSERT INTO dbo.{TABLA_BULTOS} (ParadaId, Empresa, NumTraspaso, Bulto, Total, Usuario, FechaHora, ClaveIdempotencia, FechaRegistro) " +
                            "VALUES (@ParadaId, @Empresa, @Traspaso, @Bulto, @Total, @Usuario, @FechaHora, @Clave, @FechaRegistro)",
                            new SqlParameter("@ParadaId", SqlDbType.Int) { Value = parada },
                            new SqlParameter("@Empresa", SqlDbType.Char, 3) { Value = carga.Empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO },
                            new SqlParameter("@Traspaso", SqlDbType.Int) { Value = carga.Traspaso },
                            new SqlParameter("@Bulto", SqlDbType.SmallInt) { Value = (short)carga.Bulto },
                            new SqlParameter("@Total", SqlDbType.SmallInt) { Value = (short)Math.Max(carga.Total, carga.Bulto) },
                            new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(carga.Usuario, 50) ?? string.Empty },
                            new SqlParameter("@FechaHora", SqlDbType.DateTime) { Value = carga.FechaHora },
                            new SqlParameter("@Clave", SqlDbType.UniqueIdentifier) { Value = carga.ClaveIdempotencia },
                            new SqlParameter("@FechaRegistro", SqlDbType.DateTime) { Value = carga.FechaRegistro }).ConfigureAwait(false);
                    }
                    catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
                    {
                        throw new ClaveIdempotenciaRepetidaException(ex);
                    }
                    transaccion.Commit();
                    return parada;
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
        }

        public async Task<bool> Entregar(int parada, Guid clave, DateTime fechaHora, string usuario, string receptor, string observaciones,
            decimal? latitud, decimal? longitud)
        {
            int filas = await db.Database.ExecuteSqlCommandAsync(
                $"UPDATE dbo.{TABLA} SET Estado = @Entregada, FechaEntrega = @FechaHora, UsuarioEntrega = @Usuario, ClaveEntrega = @Clave, " +
                "Receptor = @Receptor, Observaciones = @Observaciones, Latitud = @Latitud, Longitud = @Longitud " +
                "WHERE Id = @Id AND Estado = @EnReparto",
                new SqlParameter("@Entregada", SqlDbType.VarChar, 12) { Value = ParadaReposicion.ESTADO_ENTREGADA },
                new SqlParameter("@EnReparto", SqlDbType.VarChar, 12) { Value = ParadaReposicion.ESTADO_EN_REPARTO },
                new SqlParameter("@FechaHora", SqlDbType.DateTime) { Value = fechaHora },
                new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(usuario, 50) ?? string.Empty },
                new SqlParameter("@Clave", SqlDbType.UniqueIdentifier) { Value = clave },
                new SqlParameter("@Receptor", SqlDbType.NVarChar, 100) { Value = (object)receptor ?? DBNull.Value },
                new SqlParameter("@Observaciones", SqlDbType.NVarChar, 1000) { Value = (object)observaciones ?? DBNull.Value },
                Decimal("@Latitud", latitud),
                Decimal("@Longitud", longitud),
                new SqlParameter("@Id", SqlDbType.Int) { Value = parada }).ConfigureAwait(false);
            return filas > 0;
        }

        private static SqlParameter Decimal(string nombre, decimal? valor)
            => new SqlParameter(nombre, SqlDbType.Decimal) { Precision = 9, Scale = 6, Value = (object)valor ?? DBNull.Value };

        private static string Lista(IReadOnlyCollection<int> ids)
            => ids == null || ids.Count == 0 ? null : string.Join(", ", ids.Distinct().Select(i => i.ToString(CultureInfo.InvariantCulture)));

        private static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }
}
