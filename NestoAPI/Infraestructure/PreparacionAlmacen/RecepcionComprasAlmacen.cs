using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PedidosCompra;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Una línea pendiente de recibir de un pedido de compra.</summary>
    public class FilaRecepcionCompra
    {
        public int LineaPedido { get; set; }
        /// <summary>El pedido de compra de la línea (en la recepción por proveedor hay varios).</summary>
        public int Pedido { get; set; }
        public string Proveedor { get; set; }
        public string NombreProveedor { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string CodigoBarras { get; set; }
        public int Cantidad { get; set; }
    }

    public interface IRepositorioRecepcionCompras
    {
        Task<List<PedidoCompraPendienteDTO>> LeerPedidosPendientes(string empresa, string almacen);
        Task<List<FilaRecepcionCompra>> LeerLineasPendientes(string empresa, int pedido);
        /// <summary>NestoAPI#559: las líneas pendientes de todos los pedidos de un proveedor en un almacén.</summary>
        Task<List<FilaRecepcionCompra>> LeerLineasPendientesProveedor(string empresa, string almacen, string proveedor);
        /// <summary>NestoAPI#559: los proveedores con algo pendiente de recibir de ese producto (por número o código de barras).</summary>
        Task<List<string>> ProveedoresConPendiente(string empresa, string almacen, string codigo);
        /// <summary>NestoAPI#559: terminar una recepción, todo o nada.</summary>
        Task<ResultadoTerminarRecepcionDTO> EnTransaccion(Func<ITransaccionRecepcionCompra, Task<ResultadoTerminarRecepcionDTO>> trabajo);
    }

    /// <summary>
    /// NestoAPI#559: lecturas de los pedidos de compra para recibir mercancía con Ariadna. «Pendiente
    /// de recibir» = línea de producto en estado 1 (las mismas que después albaranea
    /// prdCrearAlbaránCmp, que además exige el visto bueno). Solo lectura.
    /// </summary>
    public class RepositorioRecepcionCompras : IRepositorioRecepcionCompras, IDisposable
    {
        private readonly Database baseDeDatos;
        private readonly NVEntities db;
        private readonly IPedidosCompraService pedidosCompra;
        private bool contextoPropio;

        public RepositorioRecepcionCompras(NVEntities db) : this(db, null)
        {
        }

        internal RepositorioRecepcionCompras(NVEntities db, IPedidosCompraService pedidosCompra) : this(db.Database)
        {
            this.db = db;
            this.pedidosCompra = pedidosCompra ?? new PedidosCompraService();
        }

        /// <summary>Para el contenedor de dependencias: crea su contexto y lo libera al acabar la petición.</summary>
        public static RepositorioRecepcionCompras ConContextoPropio()
        {
            return new RepositorioRecepcionCompras(new NVEntities()) { contextoPropio = true };
        }

        public void Dispose()
        {
            if (contextoPropio)
            {
                db?.Dispose();
            }
        }

        internal RepositorioRecepcionCompras(Database baseDeDatos)
        {
            this.baseDeDatos = baseDeDatos;
        }

        // Hay líneas de tipo producto sin producto (un comentario tecleado): no son mercancía. El visto bueno no
        // filtra: tampoco lo hace lo pendiente de recibir del resto del sistema (ProductoService.PendienteRecibir).
        internal const string FILTRO_LINEAS_DE_PRODUCTO =
            "l.Estado = 1 AND l.[TipoLínea] = '1' AND l.Cantidad > 0 AND l.Producto IS NOT NULL AND RTRIM(l.Producto) <> ''";

        internal const string SQL_PEDIDOS_PENDIENTES = @"
SELECT l.[Número] AS Pedido, RTRIM(MAX(l.[NºProveedor])) AS Proveedor, RTRIM(MAX(pr.Nombre)) AS NombreProveedor,
       MAX(c.Fecha) AS Fecha, MIN(l.[FechaRecepción]) AS FechaRecepcion,
       COUNT(*) AS Lineas, CAST(SUM(l.Cantidad) AS int) AS Unidades
FROM LinPedidoCmp l
     JOIN CabPedidoCmp c ON c.Empresa = l.Empresa AND c.[Número] = l.[Número]
     LEFT JOIN Proveedores pr ON pr.Empresa = l.Empresa AND pr.[Número] = l.[NºProveedor] AND pr.Contacto = l.Contacto
WHERE l.Empresa = @p0 AND l.[Almacén] = @p1 AND " + FILTRO_LINEAS_DE_PRODUCTO + @"
GROUP BY l.[Número]
ORDER BY MIN(l.[FechaRecepción]), l.[Número]";

        private const string COLUMNAS_LINEA = @"
SELECT l.[NºOrden] AS LineaPedido, l.[Número] AS Pedido, RTRIM(l.[NºProveedor]) AS Proveedor, RTRIM(pr.Nombre) AS NombreProveedor,
       RTRIM(l.Producto) AS Producto, RTRIM(l.Texto) AS Descripcion, RTRIM(p.CodBarras) AS CodigoBarras,
       CAST(l.Cantidad AS int) AS Cantidad
FROM LinPedidoCmp l
     LEFT JOIN Productos p ON p.Empresa = l.Empresa AND p.[Número] = l.Producto
     LEFT JOIN Proveedores pr ON pr.Empresa = l.Empresa AND pr.[Número] = l.[NºProveedor] AND pr.Contacto = l.Contacto";

        internal const string SQL_LINEAS_PENDIENTES = COLUMNAS_LINEA + @"
WHERE l.Empresa = @p0 AND l.[Número] = @p1 AND " + FILTRO_LINEAS_DE_PRODUCTO + @"
ORDER BY l.[NºOrden]";

        // NestoAPI#559: de qué proveedor es un producto que se lee (hoy cada producto pendiente es de un solo proveedor)
        internal const string SQL_PROVEEDORES_DEL_CODIGO = @"
SELECT DISTINCT RTRIM(l.[NºProveedor])
FROM LinPedidoCmp l
     LEFT JOIN Productos p ON p.Empresa = l.Empresa AND p.[Número] = l.Producto
WHERE l.Empresa = @p0 AND l.[Almacén] = @p1 AND (l.Producto = @p2 OR p.CodBarras = @p2) AND " + FILTRO_LINEAS_DE_PRODUCTO;

        // NestoAPI#559: la recepción es por proveedor (todos sus pedidos abiertos en el almacén)
        internal const string SQL_LINEAS_PENDIENTES_PROVEEDOR = COLUMNAS_LINEA + @"
WHERE l.Empresa = @p0 AND l.[Almacén] = @p1 AND l.[NºProveedor] = @p2 AND " + FILTRO_LINEAS_DE_PRODUCTO + @"
ORDER BY l.[Número], l.[NºOrden]";

        public Task<List<PedidoCompraPendienteDTO>> LeerPedidosPendientes(string empresa, string almacen)
        {
            return baseDeDatos.SqlQuery<PedidoCompraPendienteDTO>(SQL_PEDIDOS_PENDIENTES, empresa, almacen).ToListAsync();
        }

        public Task<List<FilaRecepcionCompra>> LeerLineasPendientes(string empresa, int pedido)
        {
            return baseDeDatos.SqlQuery<FilaRecepcionCompra>(SQL_LINEAS_PENDIENTES, empresa, pedido).ToListAsync();
        }

        public Task<List<FilaRecepcionCompra>> LeerLineasPendientesProveedor(string empresa, string almacen, string proveedor)
        {
            return baseDeDatos.SqlQuery<FilaRecepcionCompra>(SQL_LINEAS_PENDIENTES_PROVEEDOR, empresa, almacen, proveedor).ToListAsync();
        }

        public Task<List<string>> ProveedoresConPendiente(string empresa, string almacen, string codigo)
        {
            return baseDeDatos.SqlQuery<string>(SQL_PROVEEDORES_DEL_CODIGO, empresa, almacen, codigo).ToListAsync();
        }

        /// <summary>
        /// Todo o nada. prdCrearAlbaránCmp abre y cierra su propia transacción (anidada en esta) y, si falla, hace
        /// ROLLBACK de todo: entonces no queda transacción que deshacer y el error del procedimiento es lo que se cuenta.
        /// </summary>
        public async Task<ResultadoTerminarRecepcionDTO> EnTransaccion(Func<ITransaccionRecepcionCompra, Task<ResultadoTerminarRecepcionDTO>> trabajo)
        {
            if (db == null)
            {
                throw new InvalidOperationException("Para terminar una recepción hace falta el contexto de datos completo.");
            }
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    ResultadoTerminarRecepcionDTO resultado = await trabajo(new TransaccionRecepcionComprasSql(db, pedidosCompra)).ConfigureAwait(false);
                    transaccion.Commit();
                    return resultado;
                }
                catch (Exception ex)
                {
                    Deshacer(transaccion);
                    SqlException sql = BuscarSqlException(ex);
                    if (sql != null && sql.Class >= 11 && sql.Class <= 16)
                    {
                        // Avisos del propio procedimiento («No hay líneas para albaranear», ubicaciones…) o de una restricción
                        throw new NestoBusinessException(EvidenciasRecepcionSql.Traducir(sql), ex);
                    }
                    throw;
                }
            }
        }

        private static void Deshacer(DbContextTransaction transaccion)
        {
            try
            {
                transaccion.Rollback();
            }
            catch (Exception)
            {
                // El ROLLBACK del procedimiento ya la ha deshecho: no queda nada que deshacer
            }
        }

        private static SqlException BuscarSqlException(Exception ex)
        {
            for (Exception actual = ex; actual != null; actual = actual.InnerException)
            {
                if (actual is SqlException sql)
                {
                    return sql;
                }
            }
            return null;
        }
    }

    public interface IServicioRecepcionCompras
    {
        Task<List<PedidoCompraPendienteDTO>> LeerPedidosPendientes(string empresa, string almacen);
        /// <summary>Null si el pedido no tiene nada pendiente de recibir.</summary>
        Task<RecepcionCompraDTO> LeerRecepcion(string empresa, int pedido);
        /// <summary>Compara lo contado con lo que queda por recibir del pedido. No guarda nada. Null si el pedido no tiene nada pendiente.</summary>
        Task<ResultadoRecepcionCompraDTO> Casar(string empresa, int pedido, IEnumerable<LecturaRecepcionDTO> lecturas);
    }

    /// <summary>
    /// NestoAPI#559: recibir mercancía de proveedor leyendo los productos y casándolos con el pedido
    /// de compra. Este corte solo enseña lo que se espera y dice en qué se diferencia de lo contado
    /// (de más, de menos, no pedido); crear el albarán sigue siendo cosa de lo que ya existe.
    /// </summary>
    public class ServicioRecepcionCompras : IServicioRecepcionCompras, IDisposable
    {
        private readonly IRepositorioRecepcionCompras repositorio;
        private readonly NVEntities dbPropio;

        public ServicioRecepcionCompras()
        {
            dbPropio = new NVEntities();
            repositorio = new RepositorioRecepcionCompras(dbPropio);
        }

        internal ServicioRecepcionCompras(IRepositorioRecepcionCompras repositorio)
        {
            this.repositorio = repositorio;
        }

        public Task<List<PedidoCompraPendienteDTO>> LeerPedidosPendientes(string empresa, string almacen)
        {
            return repositorio.LeerPedidosPendientes(empresa, almacen);
        }

        public async Task<RecepcionCompraDTO> LeerRecepcion(string empresa, int pedido)
        {
            List<FilaRecepcionCompra> filas = await repositorio.LeerLineasPendientes(empresa, pedido).ConfigureAwait(false);
            if (!filas.Any())
            {
                return null;
            }

            HashSet<string> duplicados = CasadorEscaneos.CodigosDuplicados(
                filas.Select(f => new KeyValuePair<string, string>(f.Producto, f.CodigoBarras)));

            return new RecepcionCompraDTO
            {
                Empresa = empresa,
                Pedido = pedido,
                Proveedor = filas.First().Proveedor?.Trim(),
                NombreProveedor = filas.First().NombreProveedor?.Trim(),
                Lineas = filas.Select(f =>
                {
                    string codigo = string.IsNullOrWhiteSpace(f.CodigoBarras) ? null : f.CodigoBarras.Trim();
                    return new LineaRecepcionCompraDTO
                    {
                        LineaPedido = f.LineaPedido,
                        Producto = f.Producto?.Trim(),
                        Descripcion = f.Descripcion?.Trim(),
                        CodigoBarras = codigo,
                        SinCodigo = codigo == null,
                        CodigoDuplicado = codigo != null && duplicados.Contains(codigo),
                        Cantidad = f.Cantidad
                    };
                }).ToList()
            };
        }

        public async Task<ResultadoRecepcionCompraDTO> Casar(string empresa, int pedido, IEnumerable<LecturaRecepcionDTO> lecturas)
        {
            List<FilaRecepcionCompra> filas = await repositorio.LeerLineasPendientes(empresa, pedido).ConfigureAwait(false);
            if (!filas.Any())
            {
                return null;
            }

            List<DiferenciaPreparacionDTO> diferencias = CasadorEscaneos.Casar(
                filas.Select(f => new CasadorEscaneos.Cantidad { Producto = f.Producto, Descripcion = f.Descripcion, Unidades = f.Cantidad }),
                (lecturas ?? Enumerable.Empty<LecturaRecepcionDTO>())
                    .Where(l => !string.IsNullOrWhiteSpace(l?.Producto))
                    .Select(l => new CasadorEscaneos.Cantidad { Producto = l.Producto, Unidades = l.Cantidad }));

            return new ResultadoRecepcionCompraDTO
            {
                Pedido = pedido,
                Productos = diferencias,
                Cuadra = CasadorEscaneos.EstaCompleto(diferencias)
            };
        }

        public void Dispose()
        {
            dbPropio?.Dispose();
        }
    }
}
