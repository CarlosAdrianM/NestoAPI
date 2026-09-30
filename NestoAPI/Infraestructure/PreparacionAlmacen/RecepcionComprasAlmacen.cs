using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Una línea pendiente de recibir de un pedido de compra.</summary>
    public class FilaRecepcionCompra
    {
        public int LineaPedido { get; set; }
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
    }

    /// <summary>
    /// NestoAPI#559: lecturas de los pedidos de compra para recibir mercancía con Ariadna. «Pendiente
    /// de recibir» = línea de producto en estado 1 (las mismas que después albaranea
    /// prdCrearAlbaránCmp, que además exige el visto bueno). Solo lectura.
    /// </summary>
    public class RepositorioRecepcionCompras : IRepositorioRecepcionCompras
    {
        private readonly Database baseDeDatos;

        public RepositorioRecepcionCompras(NVEntities db) : this(db.Database)
        {
        }

        internal RepositorioRecepcionCompras(Database baseDeDatos)
        {
            this.baseDeDatos = baseDeDatos;
        }

        // Hay líneas de tipo producto sin producto (un comentario tecleado): no son mercancía
        private const string FILTRO_LINEAS_DE_PRODUCTO =
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

        internal const string SQL_LINEAS_PENDIENTES = @"
SELECT l.[NºOrden] AS LineaPedido, RTRIM(l.[NºProveedor]) AS Proveedor, RTRIM(pr.Nombre) AS NombreProveedor,
       RTRIM(l.Producto) AS Producto, RTRIM(l.Texto) AS Descripcion, RTRIM(p.CodBarras) AS CodigoBarras,
       CAST(l.Cantidad AS int) AS Cantidad
FROM LinPedidoCmp l
     LEFT JOIN Productos p ON p.Empresa = l.Empresa AND p.[Número] = l.Producto
     LEFT JOIN Proveedores pr ON pr.Empresa = l.Empresa AND pr.[Número] = l.[NºProveedor] AND pr.Contacto = l.Contacto
WHERE l.Empresa = @p0 AND l.[Número] = @p1 AND " + FILTRO_LINEAS_DE_PRODUCTO + @"
ORDER BY l.[NºOrden]";

        public Task<List<PedidoCompraPendienteDTO>> LeerPedidosPendientes(string empresa, string almacen)
        {
            return baseDeDatos.SqlQuery<PedidoCompraPendienteDTO>(SQL_PEDIDOS_PENDIENTES, empresa, almacen).ToListAsync();
        }

        public Task<List<FilaRecepcionCompra>> LeerLineasPendientes(string empresa, int pedido)
        {
            return baseDeDatos.SqlQuery<FilaRecepcionCompra>(SQL_LINEAS_PENDIENTES, empresa, pedido).ToListAsync();
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
