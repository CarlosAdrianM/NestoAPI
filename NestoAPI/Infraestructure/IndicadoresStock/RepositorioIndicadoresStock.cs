using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.IndicadoresStock
{
    /// <summary>
    /// NestoAPI#629: acceso a datos de los indicadores de stock. Las lecturas de ExtractoProducto y LinPedidoVta recorren las tablas
    /// enteras: SOLO se lanzan desde el job nocturno (<see cref="IndicadoresStockJobsService"/>), nunca desde un endpoint.
    /// </summary>
    public interface IRepositorioIndicadoresStock
    {
        /// <summary>Si existe la tabla IndicadoresStock (Scripts/Issue629_IndicadoresStock.sql).</summary>
        Task<bool> ExisteTabla();
        Task<List<ProductoIndicadorStock>> LeerProductos();
        Task<List<MovimientoStockTramo>> LeerStock(DateTime mes);
        Task<List<VentaTramo>> LeerVentas(DateTime mes);
        Task<List<ServicioAlmacen>> LeerServicio(DateTime mes);
        Task<List<MovimientoProductoComprado>> LeerMovimientosProductosComprados(DateTime mes);
        /// <summary>Borra las filas del mes y mete estas, en una transacción: relanzar un mes no duplica.</summary>
        Task Guardar(DateTime mes, IList<FilaIndicadoresStock> filas);
        /// <summary>Las filas de los meses de <paramref name="desde"/> a <paramref name="hasta"/> (incluidos); sin almacén, todos.</summary>
        Task<List<FilaIndicadoresStock>> Leer(DateTime desde, DateTime hasta, string almacen);
    }

    /// <summary>
    /// NestoAPI#629: SQL directo (la tabla IndicadoresStock no está en el EDMX). Todas las lecturas van con NOLOCK (no bloquean ni
    /// se bloquean con los guardados de pedidos) y acotadas al mes que se calcula; el tope de tiempo de cada consulta es
    /// <see cref="SEGUNDOS_CONSULTA_JOB"/> (el job) o <see cref="SEGUNDOS_CONSULTA_ENDPOINT"/> (el GET, que solo lee la tabla nueva).
    /// </summary>
    public class RepositorioIndicadoresStockSql : IRepositorioIndicadoresStock
    {
        /// <summary>Alto pero finito: una consulta que se atasque de noche no puede llegar a la mañana.</summary>
        public const int SEGUNDOS_CONSULTA_JOB = 900;
        public const int SEGUNDOS_CONSULTA_ENDPOINT = 30;

        internal const string SQL_EXISTE_TABLA = "SELECT CAST(CASE WHEN OBJECT_ID('dbo.IndicadoresStock') IS NULL THEN 0 ELSE 1 END AS bit)";

        // Empresa 1 (su ficha vale también para la 3), sin ficticios
        internal const string SQL_PRODUCTOS = @"
SELECT RTRIM(p.Número) AS Producto, CAST(ISNULL(p.Estado, 0) AS int) AS Estado,
    RTRIM(ISNULL(p.Grupo, '')) AS Grupo, RTRIM(ISNULL(p.SubGrupo, '')) AS SubGrupo,
    CAST(ISNULL(p.PrecioMedio, 0) AS decimal(19, 4)) AS PrecioMedio
FROM dbo.Productos p WITH (NOLOCK)
WHERE p.Empresa = '1' AND p.Ficticio = 0";

        // @p0 = día 1 del mes, @p1 = día 1 del mes siguiente. Tramo −1 = antes de los 12 meses; 0..11 = de M−11 a M.
        // Stock de una fecha pasada = movimientos con Fecha < fin (como la consulta de Carlos de la épica).
        internal const string SQL_STOCK = @"
SELECT RTRIM(e.Número) AS Producto, RTRIM(e.Almacén) AS Almacen,
    CASE WHEN e.Fecha < DATEADD(month, -11, @p0) THEN -1 ELSE DATEDIFF(month, DATEADD(month, -11, @p0), e.Fecha) END AS Tramo,
    ISNULL(SUM(CAST(e.Cantidad AS int)), 0) AS Cantidad,
    MIN(e.Fecha) AS PrimeraFecha,
    SUM(CASE WHEN e.Diario = '_ALBCOMP' THEN CAST(e.Cantidad AS int) ELSE 0 END) AS UnidadesCompradas,
    CAST(SUM(CASE WHEN e.Diario <> '_ALBCOMP' THEN 0 WHEN e.Cantidad < 0 THEN -ABS(ISNULL(e.Importe, 0)) ELSE ABS(ISNULL(e.Importe, 0)) END) AS decimal(19, 4)) AS ImporteCompras,
    SUM(CASE WHEN e.Diario LIKE 'Merma%' OR e.Diario = 'Robo' THEN CAST(e.Cantidad AS int) ELSE 0 END) AS UnidadesBajas
FROM dbo.ExtractoProducto e WITH (NOLOCK)
INNER JOIN dbo.Productos p WITH (NOLOCK) ON p.Empresa = '1' AND p.Número = e.Número AND p.Ficticio = 0
WHERE e.Empresa IN ('1', '3') AND ISNULL(e.Almacén, '') <> 'EXT' AND e.Fecha < @p1
GROUP BY e.Número, e.Almacén,
    CASE WHEN e.Fecha < DATEADD(month, -11, @p0) THEN -1 ELSE DATEDIFF(month, DATEADD(month, -11, @p0), e.Fecha) END";

        // Venta = lo que ha salido a clientes (estado >= 2: fuera −3, −2, −1 y 1), sin MTP ni MMP. Tramo 0..23 = de M−23 a M.
        internal const string SQL_VENTAS = @"
SELECT RTRIM(l.Producto) AS Producto, RTRIM(l.Almacén) AS Almacen,
    DATEDIFF(month, DATEADD(month, -23, @p0), l.[Fecha Albarán]) AS Tramo,
    ISNULL(SUM(CAST(l.Cantidad AS int)), 0) AS UnidadesNetas,
    SUM(CASE WHEN l.Cantidad > 0 THEN CAST(l.Cantidad AS int) ELSE 0 END) AS UnidadesSalida
FROM dbo.LinPedidoVta l WITH (NOLOCK)
INNER JOIN dbo.Productos p WITH (NOLOCK) ON p.Empresa = '1' AND p.Número = l.Producto AND p.Ficticio = 0
WHERE l.Empresa IN ('1', '3') AND l.TipoLinea = 1 AND l.Estado >= 2
    AND l.[Fecha Albarán] >= DATEADD(month, -23, @p0) AND l.[Fecha Albarán] < @p1
    AND ISNULL(p.Grupo, '') <> 'MTP' AND ISNULL(p.SubGrupo, '') <> 'MMP'
GROUP BY l.Producto, l.Almacén, DATEDIFF(month, DATEADD(month, -23, @p0), l.[Fecha Albarán])";

        // Días en servir (la consulta de Carlos: estado >= 2, TipoLinea 1, cantidad > 0) y líneas que salieron enteras: sin otra
        // línea del mismo pedido, producto y fecha de entrega (la parte que quedó al partirla) en otro albarán o aún pendiente.
        internal const string SQL_SERVICIO = @"
SELECT RTRIM(l.Almacén) AS Almacen, COUNT(*) AS Lineas,
    SUM(CAST(DATEDIFF(day, l.[Fecha Entrega], l.[Fecha Albarán]) AS bigint)) AS SumaDias,
    SUM(c.Completa) AS LineasCompletas
FROM dbo.LinPedidoVta l WITH (NOLOCK)
CROSS APPLY (SELECT CASE WHEN EXISTS (
        SELECT 1 FROM dbo.LinPedidoVta o WITH (NOLOCK)
        WHERE o.Empresa = l.Empresa AND o.Número = l.Número AND o.Producto = l.Producto AND o.[Fecha Entrega] = l.[Fecha Entrega]
            AND o.[Nº Orden] <> l.[Nº Orden] AND o.TipoLinea = 1 AND o.Cantidad > 0 AND o.Estado >= -1
            AND (o.Estado < 2 OR o.[Nº Albarán] <> l.[Nº Albarán])) THEN 0 ELSE 1 END AS Completa) c
WHERE l.Empresa IN ('1', '3') AND l.TipoLinea = 1 AND l.Estado >= 2 AND l.Cantidad > 0
    AND l.[Fecha Albarán] >= @p0 AND l.[Fecha Albarán] < @p1 AND l.[Fecha Entrega] IS NOT NULL
GROUP BY l.Almacén";

        // Los movimientos del mes de los productos que han tenido una entrada de compra en el mes
        internal const string SQL_MOVIMIENTOS_COMPRADOS = @"
SELECT RTRIM(e.Número) AS Producto, RTRIM(e.Almacén) AS Almacen, e.Fecha, RTRIM(e.Empresa) AS Empresa, e.[Nº Orden] AS Orden,
    CAST(e.Cantidad AS int) AS Cantidad,
    CAST(CASE WHEN e.Diario = '_ALBCOMP' AND e.Cantidad > 0 THEN 1 ELSE 0 END AS bit) AS EsCompra
FROM dbo.ExtractoProducto e WITH (NOLOCK)
WHERE e.Empresa IN ('1', '3') AND ISNULL(e.Almacén, '') <> 'EXT' AND e.Fecha >= @p0 AND e.Fecha < @p1
    AND e.Número IN (
        SELECT c.Número FROM dbo.ExtractoProducto c WITH (NOLOCK)
        WHERE c.Diario = '_ALBCOMP' AND c.Empresa IN ('1', '3') AND c.Cantidad > 0
            AND ISNULL(c.Almacén, '') <> 'EXT' AND c.Fecha >= @p0 AND c.Fecha < @p1)";

        internal const string SQL_BORRAR_MES = "DELETE FROM dbo.IndicadoresStock WHERE Mes = @p0";

        /// <summary>Las columnas de IndicadoresStock que escribe el job, en el orden de los parámetros del INSERT.</summary>
        internal static readonly string[] COLUMNAS =
        {
            "Mes", "Almacen", "ValorStock", "UnidadesStock", "ValorEstado4", "ProductosEstado4", "ValorEstado1", "ProductosEstado1",
            "ValorSinVenta6Meses", "ProductosSinVenta6Meses", "ValorSinVenta12Meses", "ProductosSinVenta12Meses",
            "ValorSinVenta24Meses", "ProductosSinVenta24Meses", "CosteVentas12Meses", "ValorStockMedio12Meses", "Rotacion",
            "CoberturaMediaDias", "LineasServidas", "DiasServirMedia", "NivelServicio", "ComprasValor", "ComprasUnidades",
            "BajasPerdidasValor", "BajasPerdidasUnidades", "AltasReferencias", "BajasReferencias", "UnidadesCompradasMes",
            "UnidadesCompradasCoberturaAlta", "ValorStockAlgete", "ValorStockTiendas"
        };

        internal static readonly string SQL_INSERTAR = "INSERT INTO dbo.IndicadoresStock (" + string.Join(", ", COLUMNAS) + ") VALUES ("
            + string.Join(", ", COLUMNAS.Select((c, i) => "@p" + i)) + ")";

        internal static readonly string SQL_LEER = "SELECT " + string.Join(", ", COLUMNAS) + @", FechaCalculo
FROM dbo.IndicadoresStock
WHERE Mes >= @p0 AND Mes <= @p1 AND (@p2 IS NULL OR Almacen = @p2)
ORDER BY Mes, CASE WHEN Almacen = 'TOTAL' THEN 0 ELSE 1 END, Almacen";

        private readonly NVEntities db;

        public RepositorioIndicadoresStockSql(NVEntities db, int segundosPorConsulta)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.db.Database.CommandTimeout = segundosPorConsulta;
        }

        public async Task<bool> ExisteTabla()
        {
            return await db.Database.SqlQuery<bool>(SQL_EXISTE_TABLA).FirstAsync().ConfigureAwait(false);
        }

        public Task<List<ProductoIndicadorStock>> LeerProductos()
        {
            return db.Database.SqlQuery<ProductoIndicadorStock>(SQL_PRODUCTOS).ToListAsync();
        }

        public Task<List<MovimientoStockTramo>> LeerStock(DateTime mes)
        {
            return db.Database.SqlQuery<MovimientoStockTramo>(SQL_STOCK, Inicio(mes), Fin(mes)).ToListAsync();
        }

        public Task<List<VentaTramo>> LeerVentas(DateTime mes)
        {
            return db.Database.SqlQuery<VentaTramo>(SQL_VENTAS, Inicio(mes), Fin(mes)).ToListAsync();
        }

        public Task<List<ServicioAlmacen>> LeerServicio(DateTime mes)
        {
            return db.Database.SqlQuery<ServicioAlmacen>(SQL_SERVICIO, Inicio(mes), Fin(mes)).ToListAsync();
        }

        public Task<List<MovimientoProductoComprado>> LeerMovimientosProductosComprados(DateTime mes)
        {
            return db.Database.SqlQuery<MovimientoProductoComprado>(SQL_MOVIMIENTOS_COMPRADOS, Inicio(mes), Fin(mes)).ToListAsync();
        }

        public async Task Guardar(DateTime mes, IList<FilaIndicadoresStock> filas)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_BORRAR_MES, Dia("@p0", PrimeroDeMes(mes))).ConfigureAwait(false);
                foreach (FilaIndicadoresStock fila in filas)
                {
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_INSERTAR, Parametros(PrimeroDeMes(mes), fila)).ConfigureAwait(false);
                }
                transaccion.Commit();
            }
        }

        public Task<List<FilaIndicadoresStock>> Leer(DateTime desde, DateTime hasta, string almacen)
        {
            return db.Database.SqlQuery<FilaIndicadoresStock>(SQL_LEER, Dia("@p0", PrimeroDeMes(desde)), Dia("@p1", PrimeroDeMes(hasta)),
                new SqlParameter("@p2", SqlDbType.VarChar, 5) { Value = string.IsNullOrWhiteSpace(almacen) ? (object)DBNull.Value : almacen.Trim().ToUpperInvariant() })
                .ToListAsync();
        }

        internal static object[] Parametros(DateTime mes, FilaIndicadoresStock f)
        {
            object[] valores =
            {
                mes, f.Almacen, f.ValorStock, f.UnidadesStock, f.ValorEstado4, f.ProductosEstado4, f.ValorEstado1, f.ProductosEstado1,
                f.ValorSinVenta6Meses, f.ProductosSinVenta6Meses, f.ValorSinVenta12Meses, f.ProductosSinVenta12Meses,
                f.ValorSinVenta24Meses, f.ProductosSinVenta24Meses, f.CosteVentas12Meses, f.ValorStockMedio12Meses, f.Rotacion,
                f.CoberturaMediaDias, f.LineasServidas, f.DiasServirMedia, f.NivelServicio, f.ComprasValor, f.ComprasUnidades,
                f.BajasPerdidasValor, f.BajasPerdidasUnidades, f.AltasReferencias, f.BajasReferencias, f.UnidadesCompradasMes,
                f.UnidadesCompradasCoberturaAlta, f.ValorStockAlgete, f.ValorStockTiendas
            };
            var parametros = new object[valores.Length];
            for (int i = 0; i < valores.Length; i++)
            {
                string nombre = "@p" + i;
                switch (valores[i])
                {
                    case DateTime d:
                        parametros[i] = Dia(nombre, d);
                        break;
                    case string s:
                        parametros[i] = new SqlParameter(nombre, SqlDbType.VarChar, 5) { Value = s };
                        break;
                    case int n:
                        parametros[i] = new SqlParameter(nombre, SqlDbType.Int) { Value = n };
                        break;
                    case decimal m:
                        parametros[i] = new SqlParameter(nombre, SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = m };
                        break;
                    default:
                        // null: el indicador no se ha podido calcular
                        parametros[i] = new SqlParameter(nombre, EsEntero(COLUMNAS[i]) ? SqlDbType.Int : SqlDbType.Decimal) { Value = DBNull.Value };
                        break;
                }
            }
            return parametros;
        }

        private static bool EsEntero(string columna)
        {
            return columna.StartsWith("Productos", StringComparison.Ordinal) || columna.StartsWith("Unidades", StringComparison.Ordinal)
                || columna == "LineasServidas" || columna == "ComprasUnidades" || columna == "BajasPerdidasUnidades"
                || columna == "AltasReferencias" || columna == "BajasReferencias";
        }

        internal static DateTime PrimeroDeMes(DateTime fecha) => new DateTime(fecha.Year, fecha.Month, 1);

        private static SqlParameter Inicio(DateTime mes) => new SqlParameter("@p0", SqlDbType.DateTime) { Value = PrimeroDeMes(mes) };

        private static SqlParameter Fin(DateTime mes) => new SqlParameter("@p1", SqlDbType.DateTime) { Value = PrimeroDeMes(mes).AddMonths(1) };

        private static SqlParameter Dia(string nombre, DateTime dia) => new SqlParameter(nombre, SqlDbType.Date) { Value = dia.Date };
    }
}
