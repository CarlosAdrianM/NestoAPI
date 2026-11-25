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
            List<ProductoBonificadoConCantidadRequest> lineasPedido,
            int? pedido = null,
            byte? modoServicio = null,
            bool modoCorregidoPorTienda = false)
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
                // pedido excluido (NestoAPI#262): la reserva del propio pedido no cuenta contra sí misma.
                var stockEnAlmacen = await productoService.CalcularStockProducto(producto.ProductoId, almacen.Trim(), pedido).ConfigureAwait(false);

                if (stockEnAlmacen.CantidadDisponible < producto.Cantidad)
                {
                    string almacenConStock = null;
                    foreach (var sede in Constantes.Sedes.ListaSedes)
                    {
                        if (sede.Trim() == almacen.Trim()) continue;
                        var stockOtroAlmacen = await productoService.CalcularStockProducto(producto.ProductoId, sede, pedido).ConfigureAwait(false);
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
                return new ValidarServirJuntoResponse
                {
                    PuedeDesmarcar = false,
                    ProductosProblematicos = productosProblematicos,
                    Mensaje = ConstruirMensaje(productosProblematicos, modoServicio, almacen, modoCorregidoPorTienda)
                };
            }

            return new ValidarServirJuntoResponse
            {
                PuedeDesmarcar = true,
                ProductosProblematicos = new List<ProductoSinStockDTO>(),
                Mensaje = null
            };
        }

        /// <summary>
        /// El mensaje de los bonificados sin stock. Incidencia 1460 (25/11/26): en una tienda solo vale «Según vaya entrando», así que
        /// la salida no es «elija otro modo» sino cambiarlos o pasar el pedido desde Algete; y si el modo lo cambió la API
        /// (<paramref name="modoCorregidoPorTienda"/>), se dice.
        /// </summary>
        internal static string ConstruirMensaje(List<ProductoSinStockDTO> productosProblematicos, byte? modoServicio, string almacen,
            bool modoCorregidoPorTienda = false)
        {
            var listaProductos = string.Join(", ", productosProblematicos.Select(p =>
                string.IsNullOrEmpty(p.AlmacenConStock)
                    ? p.ProductoNombre
                    : $"{p.ProductoNombre} (stock en {p.AlmacenConStock})"));

            if (TextosServirJuntoTienda.EsTienda(almacen))
            {
                string sede = TextosServirJuntoTienda.Sede(almacen);
                return modoCorregidoPorTienda
                    ? $"{TextosServirJuntoTienda.ArranqueModoCorregido(almacen)} Estos productos bonificados no tienen stock suficiente en {sede}: " +
                      $"{listaProductos}. Cámbielos por otros con stock en {sede} o pase el pedido desde {TextosServirJuntoTienda.Algete()}."
                    : $"No se puede pasar el pedido a «{Constantes.Pedidos.ModosServicio.NombreDestino(modoServicio)}» porque los siguientes productos bonificados " +
                      $"no tienen stock suficiente en {almacen}: {listaProductos}. " +
                      $"En {sede} el pedido solo puede salir «{TextosServirJuntoTienda.ModoTienda()}»: cambie los productos bonificados por otros con stock " +
                      $"en {almacen} o pase el pedido desde {TextosServirJuntoTienda.Algete()}.";
            }

            // NestoAPI#482: se nombra el modo elegido; la casilla «Servir junto» ya no existe en Nesto.
            return $"No se puede pasar el pedido a «{Constantes.Pedidos.ModosServicio.NombreDestino(modoServicio)}» porque los siguientes productos bonificados " +
                   $"no tienen stock suficiente en {almacen}: {listaProductos}. " +
                   // NestoAPI#491: solo se deniega «Según vaya entrando»; cualquiera de los otros modos vale.
                   $"Cambie los productos bonificados por otros con stock en {almacen} o elija otro modo de servicio ({Constantes.Pedidos.ModosServicio.AlternativasSinValidacion()}).";
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
