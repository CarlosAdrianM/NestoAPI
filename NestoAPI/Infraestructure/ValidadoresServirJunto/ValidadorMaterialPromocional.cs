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
    /// Bloquea el desmarcado de "Servir junto" si alguna línea del pedido con subgrupo
    /// MMP (material promocional / muestras) se quedaría pendiente. A diferencia del
    /// ValidadorDisponibilidadRegalos, para MMP no vale que haya stock en otro almacén:
    /// la única solución es borrar ese producto del pedido.
    ///
    /// Issue NestoAPI#161. Se alimenta de <c>lineasPedido</c> (no de los bonificados)
    /// porque las muestras son líneas normales del pedido, no regalos. Si
    /// <c>lineasPedido</c> viene null o vacío (NestoApp u otros clientes no actualizados),
    /// la validación se salta silenciosamente → comportamiento retrocompatible.
    /// </summary>
    public class ValidadorMaterialPromocional : IValidadorServirJunto
    {
        private readonly NVEntities db;
        private readonly IProductoService productoService;

        public ValidadorMaterialPromocional(NVEntities db, IProductoService productoService)
        {
            this.db = db;
            this.productoService = productoService;
        }

        public async Task<ValidarServirJuntoResponse> Validar(
            string almacen,
            List<ProductoBonificadoConCantidadRequest> productos,
            List<ProductoBonificadoConCantidadRequest> lineasPedido)
        {
            if (lineasPedido == null || !lineasPedido.Any())
            {
                return PuedeDesmarcar();
            }

            var lineasIds = lineasPedido.Select(l => l.ProductoId).ToList();

            var productosMMP = await db.Productos
                .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                         && lineasIds.Contains(p.Número)
                         && p.SubGrupo == Constantes.Productos.SUBGRUPO_MUESTRAS)
                .Select(p => new { p.Número, p.Nombre })
                .ToListAsync()
                .ConfigureAwait(false);

            if (!productosMMP.Any())
            {
                return PuedeDesmarcar();
            }

            var idsMMP = productosMMP.Select(p => p.Número.Trim()).ToHashSet();
            var productosProblematicos = new List<ProductoSinStockDTO>();

            foreach (var linea in lineasPedido)
            {
                var productoIdTrimmed = linea.ProductoId?.Trim();
                if (productoIdTrimmed == null || !idsMMP.Contains(productoIdTrimmed)) continue;

                var stockEnAlmacen = await productoService.CalcularStockProducto(linea.ProductoId, almacen.Trim()).ConfigureAwait(false);
                if (stockEnAlmacen.CantidadDisponible >= linea.Cantidad) continue;

                var nombreProducto = productosMMP
                    .Where(p => p.Número.Trim() == productoIdTrimmed)
                    .Select(p => p.Nombre?.Trim())
                    .FirstOrDefault() ?? productoIdTrimmed;

                productosProblematicos.Add(new ProductoSinStockDTO
                {
                    ProductoId = productoIdTrimmed,
                    ProductoNombre = nombreProducto,
                    AlmacenConStock = null
                });
            }

            if (!productosProblematicos.Any())
            {
                return PuedeDesmarcar();
            }

            var listaProductos = string.Join(", ", productosProblematicos.Select(p => p.ProductoNombre));
            return new ValidarServirJuntoResponse
            {
                PuedeDesmarcar = false,
                ProductosProblematicos = productosProblematicos,
                Mensaje = $"El producto {listaProductos}, que es material promocional, se quedaría pendiente, " +
                          $"lo que no está permitido. Borre primero el producto {listaProductos} " +
                          $"para poder desmarcar el servir junto."
            };
        }

        private static ValidarServirJuntoResponse PuedeDesmarcar() =>
            new ValidarServirJuntoResponse
            {
                PuedeDesmarcar = true,
                ProductosProblematicos = new List<ProductoSinStockDTO>(),
                Mensaje = null
            };
    }
}
