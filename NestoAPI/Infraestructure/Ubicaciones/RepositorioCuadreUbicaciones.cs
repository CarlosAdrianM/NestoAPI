using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Ubicaciones
{
    /// <summary>NestoAPI#594 (corte 2): lo que el cuadre lee de la BD. Solo lectura.</summary>
    public interface IRepositorioCuadreUbicaciones
    {
        /// <summary>Almacenes.ControlUbicaciones de ese almacén en esa empresa (null si el almacén no existe).</summary>
        Task<bool?> TieneControlUbicaciones(string empresa, string almacen);

        /// <param name="producto">null: todos los productos del almacén (consulta pesada: fuera de horario).</param>
        Task<List<StockExtractoProducto>> LeerStock(string empresa, string espejo, string almacen, string producto);

        /// <param name="producto">null: todos los productos del almacén (consulta pesada: fuera de horario).</param>
        Task<List<UbicacionesPorEstado>> LeerUbicaciones(string empresa, string espejo, string almacen, string producto);

        /// <param name="producto">null: todos los de la empresa.</param>
        Task<List<ProductoCuadre>> LeerProductos(string empresa, string producto);
    }

    /// <summary>
    /// NestoAPI#594 (corte 2): el cuadre de Ubicaciones contra ExtractoProducto, sobre SQL de solo lectura con
    /// <c>WITH (NOLOCK)</c> (no bloquea a nadie; a cambio puede leer una operación a medias, así que un descuadre
    /// puntual se confirma repitiendo la consulta).
    ///
    /// <para>Índices que usa (sys.indexes de NV, 14/10/26):</para>
    /// <list type="bullet">
    /// <item>ExtractoProducto <c>_dta_index_ExtractoProducto_5_132963600__K1_K3_K12_14</c> (Empresa, Número, Almacén) INCLUDE
    /// (Cantidad): con producto, búsqueda directa; sin producto, recorre solo las filas de las dos empresas en ese índice
    /// estrecho (no la tabla, que es la de 4,2 millones de filas).</item>
    /// <item>Ubicaciones <c>_dta_index_Ubicaciones_5_50099219__K6_K1_K13_K8_K9_K10_7_14</c> (Número, Empresa, Estado, …)
    /// INCLUDE (Cantidad): con producto, búsqueda directa. Sin producto no hay índice con Almacén en la clave: recorre
    /// uno de los índices que cubren la consulta (2,7 millones de filas).</item>
    /// </list>
    /// <para>Con producto es una consulta de milisegundos. La de TODO EL ALMACÉN es PESADA: para fuera de horario (con la
    /// gente trabajando, el 17/09/26 una consulta sin filtrar bloqueó a los usuarios). Por eso lleva su propio tiempo de
    /// espera, más largo.</para>
    /// </summary>
    public class RepositorioCuadreUbicacionesSql : IRepositorioCuadreUbicaciones
    {
        /// <summary>Segundos de espera de la consulta del almacén entero (la de un producto usa el de siempre).</summary>
        internal const int TIMEOUT_ALMACEN_ENTERO = 600;

        internal const string SQL_CONTROL_UBICACIONES = @"
SELECT CAST(ControlUbicaciones AS bit) FROM Almacenes WITH (NOLOCK) WHERE Empresa = @p0 AND [Número] = @p1";

        internal const string FILTRO_PRODUCTO_EXTRACTO = " AND e.[Número] = @p3";
        internal const string FILTRO_PRODUCTO_UBICACIONES = " AND u.[Número] = @p3";
        internal const string FILTRO_PRODUCTO_PRODUCTOS = " AND p.[Número] = @p1";

        // {0}: FILTRO_PRODUCTO_EXTRACTO o nada. Empresa y su espejo juntas, como prdQuedaDescuadradaUbicación.
        internal const string SQL_STOCK = @"
SELECT RTRIM(e.[Número]) AS Producto, SUM(CAST(e.Cantidad AS int)) AS Stock
FROM ExtractoProducto e WITH (NOLOCK)
WHERE e.Empresa IN (@p0, @p1) AND e.[Almacén] = @p2{0}
GROUP BY e.[Número]";

        // {0}: FILTRO_PRODUCTO_UBICACIONES o nada. Solo lo que es mercancía (Estado >= 0; NULL como 0, igual que la puerta):
        // las negativas son histórico y son la mayoría de las filas.
        internal const string SQL_UBICACIONES = @"
SELECT RTRIM(u.[Número]) AS Producto, ISNULL(u.Estado, 0) AS Estado, SUM(u.Cantidad) AS Cantidad, COUNT(*) AS Filas
FROM Ubicaciones u WITH (NOLOCK)
WHERE u.Empresa IN (@p0, @p1) AND u.[Almacén] = @p2 AND ISNULL(u.Estado, 0) >= 0{0}
GROUP BY u.[Número], ISNULL(u.Estado, 0)";

        // {0}: FILTRO_PRODUCTO_PRODUCTOS o nada
        internal const string SQL_PRODUCTOS = @"
SELECT RTRIM(p.[Número]) AS Producto, RTRIM(p.Nombre) AS Nombre, CAST(p.Ubicar AS bit) AS Ubicar
FROM Productos p WITH (NOLOCK)
WHERE p.Empresa = @p0{0}";

        private readonly NVEntities db;

        public RepositorioCuadreUbicacionesSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<bool?> TieneControlUbicaciones(string empresa, string almacen)
        {
            return await db.Database.SqlQuery<bool?>(SQL_CONTROL_UBICACIONES, Char("@p0", empresa, 3), Char("@p1", almacen, 3))
                .FirstOrDefaultAsync().ConfigureAwait(false);
        }

        public async Task<List<StockExtractoProducto>> LeerStock(string empresa, string espejo, string almacen, string producto)
        {
            AjustarTimeout(producto);
            return await db.Database.SqlQuery<StockExtractoProducto>(Sql(SQL_STOCK, FILTRO_PRODUCTO_EXTRACTO, producto),
                Parametros(empresa, espejo, almacen, producto)).ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<UbicacionesPorEstado>> LeerUbicaciones(string empresa, string espejo, string almacen, string producto)
        {
            AjustarTimeout(producto);
            return await db.Database.SqlQuery<UbicacionesPorEstado>(Sql(SQL_UBICACIONES, FILTRO_PRODUCTO_UBICACIONES, producto),
                Parametros(empresa, espejo, almacen, producto)).ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<ProductoCuadre>> LeerProductos(string empresa, string producto)
        {
            var parametros = new List<SqlParameter> { Char("@p0", empresa, 3) };
            if (!string.IsNullOrWhiteSpace(producto))
            {
                parametros.Add(Char("@p1", producto.Trim(), 15));
            }
            return await db.Database.SqlQuery<ProductoCuadre>(Sql(SQL_PRODUCTOS, FILTRO_PRODUCTO_PRODUCTOS, producto), parametros.ToArray())
                .ToListAsync().ConfigureAwait(false);
        }

        internal static string Sql(string plantilla, string filtroProducto, string producto)
        {
            return string.Format(plantilla, string.IsNullOrWhiteSpace(producto) ? string.Empty : filtroProducto);
        }

        private void AjustarTimeout(string producto)
        {
            if (string.IsNullOrWhiteSpace(producto))
            {
                db.Database.CommandTimeout = TIMEOUT_ALMACEN_ENTERO;
            }
        }

        private static SqlParameter[] Parametros(string empresa, string espejo, string almacen, string producto)
        {
            var parametros = new List<SqlParameter>
            {
                Char("@p0", empresa, 3), Char("@p1", espejo ?? empresa, 3), Char("@p2", almacen, 3)
            };
            if (!string.IsNullOrWhiteSpace(producto))
            {
                parametros.Add(Char("@p3", producto.Trim(), 15));
            }
            return parametros.ToArray();
        }

        private static SqlParameter Char(string nombre, string valor, int longitud)
        {
            return new SqlParameter(nombre, SqlDbType.Char, longitud) { Value = (object)valor?.Trim() ?? DBNull.Value };
        }
    }

    /// <summary>NestoAPI#594 (corte 2): el servicio del cuadre (lo que llama el controlador).</summary>
    public interface IServicioCuadreUbicaciones
    {
        /// <summary>
        /// El cuadre de un producto (cuadre o no) o, sin producto, solo los descuadrados de todo el almacén (PESADA: fuera de
        /// horario). Lanza <see cref="ArgumentException"/> si el almacén no existe o no lleva control de ubicaciones.
        /// </summary>
        Task<List<CuadreUbicacionProducto>> Cuadrar(string empresa, string almacen, string producto);
    }

    public class ServicioCuadreUbicaciones : IServicioCuadreUbicaciones
    {
        private readonly IRepositorioCuadreUbicaciones repositorio;
        private readonly string espejo;

        public ServicioCuadreUbicaciones(IRepositorioCuadreUbicaciones repositorio, string espejo = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.espejo = espejo;
        }

        public async Task<List<CuadreUbicacionProducto>> Cuadrar(string empresa, string almacen, string producto)
        {
            empresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            almacen = almacen?.Trim().ToUpperInvariant();
            producto = string.IsNullOrWhiteSpace(producto) ? null : producto.Trim();
            if (string.IsNullOrWhiteSpace(almacen))
            {
                throw new ArgumentException("Falta el almacén.");
            }
            bool? control = await repositorio.TieneControlUbicaciones(empresa, almacen).ConfigureAwait(false);
            if (control == null)
            {
                throw new ArgumentException($"El almacén {almacen} no existe en la empresa {empresa}.");
            }
            if (control == false)
            {
                throw new ArgumentException($"El almacén {almacen} no lleva control de ubicaciones: no hay nada que cuadrar.");
            }

            // Secuencial: el mismo contexto no admite dos consultas a la vez
            List<StockExtractoProducto> stocks = await repositorio.LeerStock(empresa, espejo, almacen, producto).ConfigureAwait(false);
            List<UbicacionesPorEstado> ubicaciones = await repositorio.LeerUbicaciones(empresa, espejo, almacen, producto).ConfigureAwait(false);
            List<ProductoCuadre> productos = await repositorio.LeerProductos(empresa, producto).ConfigureAwait(false);
            return CuadreUbicaciones.Cuadrar(almacen, stocks, ubicaciones, productos, soloDescuadrados: producto == null);
        }
    }
}
