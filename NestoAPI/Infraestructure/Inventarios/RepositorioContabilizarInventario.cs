using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Inventarios
{
    /// <summary>NestoAPI#790: una fila pendiente (Estado 1, sin NºTraspaso) del inventario de un almacén, con lo de su producto.</summary>
    public class FilaInventarioPendiente
    {
        public int NumeroOrden { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public int? StockCalculado { get; set; }
        public int? StockReal { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
        /// <summary>El grupo del producto (el de la fila si el producto no existe).</summary>
        public string Grupo { get; set; }
        public bool Ficticio { get; set; }
        public bool Ubicar { get; set; }
        public int EstadoProducto { get; set; }
        public decimal? PrecioMedio { get; set; }
        /// <summary>StockReal (≠ 0) es la referencia de un producto de la empresa: se ha leído la referencia en la cantidad.</summary>
        public bool StockRealEsReferencia { get; set; }
    }

    /// <summary>NestoAPI#790: una fila de Ubicaciones (estado ≥ 0) del almacén.</summary>
    public class UbicacionAlmacenInventario
    {
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>NestoAPI#790: una fila del inventario con diferencia y los dos valores unitarios (el servicio elige uno).</summary>
    public class LineaInventarioConDiferencia
    {
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string Grupo { get; set; }
        public string Familia { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public int StockCalculado { get; set; }
        public int StockReal { get; set; }
        public string Usuario { get; set; }
        public decimal PVP { get; set; }
        /// <summary>Contabilizada: Inventarios.Valor (el PrecioMedio que puso el SP al contabilizar); pendiente: el PrecioMedio de hoy.</summary>
        public decimal Coste { get; set; }
    }

    public interface IRepositorioContabilizarInventario
    {
        /// <summary>Almacenes.ControlUbicaciones; null si el almacén no existe en la empresa.</summary>
        Task<bool?> LeerControlUbicaciones(string empresa, string almacen);
        /// <summary>TODAS las filas pendientes (Estado 1, sin NºTraspaso) del almacén, de cualquier fecha.</summary>
        Task<List<FilaInventarioPendiente>> LeerPendientes(string empresa, string almacen);
        /// <summary>Las filas de Ubicaciones del almacén con estado ≥ 0 (solo se pide en almacenes con control de ubicaciones).</summary>
        Task<List<UbicacionAlmacenInventario>> LeerUbicaciones(string almacen);
        /// <summary>
        /// EXEC prdContabilizarInventario @Empresa, @Usuario, @Delegación = <paramref name="almacen"/>, @FormaVenta = 'VAR'. ÚNICO call
        /// site del SP. Devuelve su return (1 bien); si el SP hace RAISERROR, lanza la SqlException.
        /// </summary>
        Task<int> Contabilizar(string empresa, string usuario, string almacen);
        /// <summary>El traspaso que acaba de poner el SP a las filas del usuario en ese almacén y día (MAX de las que están en Estado 2).</summary>
        Task<int?> LeerTraspaso(string empresa, string almacen, string usuario, DateTime fecha);
        /// <summary>Filas con StockReal ≠ StockCalculado: con traspasos, las contabilizadas en ellos; sin traspasos, las pendientes del día.</summary>
        Task<List<LineaInventarioConDiferencia>> LeerDiferencias(string empresa, string almacen, DateTime? fecha, IList<int> traspasos);
    }

    /// <summary>
    /// NestoAPI#790: SQL directo. Las comprobaciones son las consultas con las que Carlos revisaba el inventario en SSMS antes de
    /// lanzar el SP a mano (épica #784); aquí solo se leen las filas y la lógica va en <see cref="ComprobadorInventario"/>.
    /// </summary>
    public class RepositorioContabilizarInventarioSql : IRepositorioContabilizarInventario
    {
        /// <summary>El SP de un almacén entero (Algete, miles de filas con ubicaciones) puede tardar: generoso pero finito.</summary>
        public const int SEGUNDOS_CONTABILIZAR = 600;
        public const int SEGUNDOS_CONSULTA = 120;
        public const string FORMA_VENTA = "VAR";

        internal const string SQL_CONTROL_UBICACIONES = @"
SELECT CAST(ISNULL(a.ControlUbicaciones, 0) AS bit) FROM dbo.Almacenes a WHERE a.Empresa = @p0 AND a.Número = @p1";

        // StockRealEsReferencia: r.Número es char(15), así que «= CAST(... AS varchar)» compara con el relleno y va por la clave.
        // StockReal 0 no cuenta (existe la referencia «0»).
        internal const string SQL_PENDIENTES = @"
SELECT i.NºOrden AS NumeroOrden, RTRIM(i.Número) AS Producto, RTRIM(ISNULL(p.Nombre, i.Descripción)) AS Descripcion,
    i.StockCalculado, i.StockReal, RTRIM(i.Pasillo) AS Pasillo, RTRIM(i.Fila) AS Fila, RTRIM(i.Columna) AS Columna,
    RTRIM(i.Usuario) AS Usuario, i.Fecha, RTRIM(ISNULL(p.Grupo, i.Grupo)) AS Grupo,
    CAST(ISNULL(p.Ficticio, 0) AS bit) AS Ficticio, CAST(ISNULL(p.Ubicar, 0) AS bit) AS Ubicar,
    CAST(ISNULL(p.Estado, 0) AS int) AS EstadoProducto, CAST(p.PrecioMedio AS decimal(19, 4)) AS PrecioMedio,
    CAST(CASE WHEN ISNULL(i.StockReal, 0) <> 0 AND EXISTS (
        SELECT 1 FROM dbo.Productos r WHERE r.Empresa = i.Empresa AND r.Número = CAST(i.StockReal AS varchar(15))
    ) THEN 1 ELSE 0 END AS bit) AS StockRealEsReferencia
FROM dbo.Inventarios i
LEFT JOIN dbo.Productos p ON p.Empresa = i.Empresa AND p.Número = i.Número
WHERE i.Empresa = @p0 AND i.Almacén = @p1 AND i.Estado = 1 AND i.NºTraspaso IS NULL";

        // Como el SP: Ubicaciones sin filtrar por empresa (en ALG hay alguna de la empresa 3).
        internal const string SQL_UBICACIONES = @"
SELECT RTRIM(u.Pasillo) AS Pasillo, RTRIM(u.Fila) AS Fila, RTRIM(u.Columna) AS Columna, RTRIM(u.Número) AS Producto,
    RTRIM(p.Nombre) AS Descripcion, u.Cantidad
FROM dbo.Ubicaciones u
LEFT JOIN dbo.Productos p ON p.Empresa = u.Empresa AND p.Número = u.Número
WHERE u.Almacén = @p0 AND u.Estado >= 0 AND u.Pasillo IS NOT NULL";

        internal const string SQL_CONTABILIZAR = "EXEC @Resultado = dbo.prdContabilizarInventario @Empresa, @Usuario, @Delegacion, @FormaVenta";

        internal const string SQL_TRASPASO = @"
SELECT MAX(i.NºTraspaso) FROM dbo.Inventarios i
WHERE i.Empresa = @p0 AND i.Almacén = @p1 AND i.Usuario = @p2 AND i.Estado = 2 AND i.Fecha >= @p3 AND i.Fecha < @p4";

        internal const string SQL_DIFERENCIAS = @"
SELECT RTRIM(i.Número) AS Producto, RTRIM(ISNULL(p.Nombre, i.Descripción)) AS Descripcion,
    RTRIM(ISNULL(p.Grupo, i.Grupo)) AS Grupo, RTRIM(ISNULL(p.Familia, i.Familia)) AS Familia,
    RTRIM(i.Pasillo) AS Pasillo, RTRIM(i.Fila) AS Fila, RTRIM(i.Columna) AS Columna,
    ISNULL(i.StockCalculado, 0) AS StockCalculado, ISNULL(i.StockReal, 0) AS StockReal, RTRIM(i.Usuario) AS Usuario,
    CAST(ISNULL(p.PVP, 0) AS decimal(19, 4)) AS PVP,
    CAST(CASE WHEN i.Estado = 2 THEN i.Valor ELSE ISNULL(p.PrecioMedio, 0) END AS decimal(19, 4)) AS Coste
FROM dbo.Inventarios i
LEFT JOIN dbo.Productos p ON p.Empresa = i.Empresa AND p.Número = i.Número
WHERE i.Empresa = @p0 AND i.Almacén = @p1 AND ISNULL(i.StockReal, 0) <> ISNULL(i.StockCalculado, 0) AND ";

        internal const string SQL_FILTRO_PENDIENTES = "i.Estado = 1 AND i.NºTraspaso IS NULL AND i.Fecha >= @p2 AND i.Fecha < @p3";

        private readonly NVEntities db;

        public RepositorioContabilizarInventarioSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<bool?> LeerControlUbicaciones(string empresa, string almacen)
        {
            db.Database.CommandTimeout = SEGUNDOS_CONSULTA;
            List<bool> control = await db.Database.SqlQuery<bool>(SQL_CONTROL_UBICACIONES, empresa, almacen).ToListAsync().ConfigureAwait(false);
            return control.Count == 0 ? (bool?)null : control[0];
        }

        public async Task<List<FilaInventarioPendiente>> LeerPendientes(string empresa, string almacen)
        {
            db.Database.CommandTimeout = SEGUNDOS_CONSULTA;
            return await db.Database.SqlQuery<FilaInventarioPendiente>(SQL_PENDIENTES, empresa, almacen).ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<UbicacionAlmacenInventario>> LeerUbicaciones(string almacen)
        {
            db.Database.CommandTimeout = SEGUNDOS_CONSULTA;
            return await db.Database.SqlQuery<UbicacionAlmacenInventario>(SQL_UBICACIONES, almacen).ToListAsync().ConfigureAwait(false);
        }

        public async Task<int> Contabilizar(string empresa, string usuario, string almacen)
        {
            var resultado = new SqlParameter("@Resultado", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var parametros = new object[]
            {
                resultado,
                new SqlParameter("@Empresa", SqlDbType.Char, 3) { Value = empresa },
                new SqlParameter("@Usuario", SqlDbType.VarChar, 30) { Value = usuario },
                new SqlParameter("@Delegacion", SqlDbType.Char, 3) { Value = almacen },
                new SqlParameter("@FormaVenta", SqlDbType.Char, 3) { Value = FORMA_VENTA }
            };
            db.Database.CommandTimeout = SEGUNDOS_CONTABILIZAR;
            // Sin la transacción que EF abre por defecto: el SP lleva la suya y hace ROLLBACK si falla (dentro de una de EF saldría
            // el ruido del error 266 y el ROLLBACK del SP se llevaría también la de fuera).
            _ = await db.Database.ExecuteSqlCommandAsync(TransactionalBehavior.DoNotEnsureTransaction, SQL_CONTABILIZAR, parametros).ConfigureAwait(false);
            return resultado.Value is int r ? r : -1;
        }

        public async Task<int?> LeerTraspaso(string empresa, string almacen, string usuario, DateTime fecha)
        {
            db.Database.CommandTimeout = SEGUNDOS_CONSULTA;
            DateTime desde = fecha.Date;
            List<int?> traspaso = await db.Database.SqlQuery<int?>(SQL_TRASPASO, empresa, almacen, usuario, desde, desde.AddDays(1))
                .ToListAsync().ConfigureAwait(false);
            return traspaso.FirstOrDefault();
        }

        public async Task<List<LineaInventarioConDiferencia>> LeerDiferencias(string empresa, string almacen, DateTime? fecha, IList<int> traspasos)
        {
            db.Database.CommandTimeout = SEGUNDOS_CONSULTA;
            var parametros = new List<object> { empresa, almacen };
            string filtro;
            if (traspasos != null && traspasos.Count > 0)
            {
                // Cada traspaso va como parámetro: @p2, @p3...
                filtro = "i.Estado = 2 AND i.NºTraspaso IN (" + string.Join(", ", traspasos.Select((t, n) => "@p" + (n + 2))) + ")";
                parametros.AddRange(traspasos.Cast<object>());
            }
            else
            {
                if (!fecha.HasValue)
                {
                    throw new ArgumentException("Hace falta la fecha o el traspaso.", nameof(fecha));
                }
                filtro = SQL_FILTRO_PENDIENTES;
                parametros.Add(fecha.Value.Date);
                parametros.Add(fecha.Value.Date.AddDays(1));
            }
            return await db.Database.SqlQuery<LineaInventarioConDiferencia>(SQL_DIFERENCIAS + filtro, parametros.ToArray())
                .ToListAsync().ConfigureAwait(false);
        }
    }
}
