using NestoAPI.Infraestructure.Kits;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta.ServirJunto;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ValidadoresServirJunto
{
    /// <summary>
    /// Valida que los productos bonificados tengan disponibilidad suficiente en el almacén del pedido.
    /// Issue #141: Compara CantidadDisponible vs Cantidad solicitada (antes solo verificaba <= 0).
    /// </summary>
    public class ValidadorDisponibilidadRegalos : IValidadorServirJunto
    {
        private readonly NVEntities db;
        private readonly IProductoService productoService;

        public ValidadorDisponibilidadRegalos(NVEntities db, IProductoService productoService)
        {
            this.db = db;
            this.productoService = productoService;
        }

        public async Task<ValidarServirJuntoResponse> Validar(
            string almacen,
            List<ProductoBonificadoConCantidadRequest> productos,
            List<ProductoBonificadoConCantidadRequest> lineasPedido)
        {
            // NestoAPI#175: además de los bonificados explícitos (productos), considerar
            // las líneas del pedido marcadas como EsBonificadoGanavisiones. Cierra el
            // agujero de DetallePedido, donde ProductosBonificadosConCantidad viene vacío
            // y los bonificados llegan como líneas normales del pedido.
            productos = UnificarConBonificadosDeLineasPedido(productos, lineasPedido);
            var productosIds = productos.Select(p => p.ProductoId).ToList();

            var nombresProductos = await db.Productos
                .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && productosIds.Contains(p.Número))
                .Select(p => new { p.Número, p.Nombre })
                .ToListAsync()
                .ConfigureAwait(false);

            var productosProblematicos = new List<ProductoSinStockDTO>();
            foreach (var producto in productos)
            {
                var productoIdTrimmed = producto.ProductoId?.Trim();
                var stockEnAlmacen = await productoService.CalcularStockProducto(producto.ProductoId, almacen.Trim()).ConfigureAwait(false);

                if (stockEnAlmacen.CantidadDisponible < producto.Cantidad)
                {
                    string almacenConStock = null;
                    foreach (var sede in Constantes.Sedes.ListaSedes)
                    {
                        if (sede.Trim() == almacen.Trim()) continue;
                        var stockOtroAlmacen = await productoService.CalcularStockProducto(producto.ProductoId, sede).ConfigureAwait(false);
                        if (stockOtroAlmacen.CantidadDisponible >= producto.Cantidad)
                        {
                            almacenConStock = sede;
                            break;
                        }
                    }

                    var nombreProducto = nombresProductos
                        .Where(p => p.Número.Trim() == productoIdTrimmed)
                        .Select(p => p.Nombre?.Trim())
                        .FirstOrDefault() ?? productoIdTrimmed;

                    productosProblematicos.Add(new ProductoSinStockDTO
                    {
                        ProductoId = producto.ProductoId?.Trim(),
                        ProductoNombre = nombreProducto,
                        AlmacenConStock = almacenConStock?.Trim()
                    });
                }
            }

            if (productosProblematicos.Any())
            {
                var listaProductos = string.Join(", ", productosProblematicos.Select(p =>
                    string.IsNullOrEmpty(p.AlmacenConStock)
                        ? p.ProductoNombre
                        : $"{p.ProductoNombre} (stock en {p.AlmacenConStock})"));

                return new ValidarServirJuntoResponse
                {
                    PuedeDesmarcar = false,
                    ProductosProblematicos = productosProblematicos,
                    Mensaje = $"No se puede desmarcar 'Servir junto' porque los siguientes productos bonificados " +
                              $"no tienen stock suficiente en {almacen}: {listaProductos}. " +
                              $"Cambie los productos bonificados por otros con stock en {almacen} o mantenga 'Servir junto' marcado."
                };
            }

            return new ValidarServirJuntoResponse
            {
                PuedeDesmarcar = true,
                ProductosProblematicos = new List<ProductoSinStockDTO>(),
                Mensaje = null
            };
        }

        private static List<ProductoBonificadoConCantidadRequest> UnificarConBonificadosDeLineasPedido(
            List<ProductoBonificadoConCantidadRequest> productos,
            List<ProductoBonificadoConCantidadRequest> lineasPedido)
        {
            if (lineasPedido == null || !lineasPedido.Any())
            {
                return productos;
            }

            var idsExistentes = new HashSet<string>(productos.Select(p => p.ProductoId?.Trim()));
            var unificados = new List<ProductoBonificadoConCantidadRequest>(productos);

            foreach (var linea in lineasPedido.Where(l => l.EsBonificadoGanavisiones))
            {
                if (idsExistentes.Add(linea.ProductoId?.Trim()))
                {
                    unificados.Add(linea);
                }
            }

            return unificados;
        }
    }
}
