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
            List<ProductoBonificadoConCantidadRequest> lineasPedido,
            int? pedido = null,
            byte? modoServicio = null,
            bool modoCorregidoPorTienda = false)
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

                // pedido excluido (NestoAPI#262): la reserva de la propia línea del pedido no debe contar
                // contra sí misma. Sin esto se denegaba aunque hubiera stock libre suficiente.
                var stockEnAlmacen = await productoService.CalcularStockProducto(linea.ProductoId, almacen.Trim(), pedido).ConfigureAwait(false);
                if (stockEnAlmacen.CantidadDisponible >= linea.Cantidad) continue;

                var nombreProducto = productosMMP
                    .Where(p => p.Número.Trim() == productoIdTrimmed)
                    .Select(p => p.Nombre?.Trim())
                    .FirstOrDefault() ?? productoIdTrimmed;

                productosProblematicos.Add(new ProductoSinStockDTO
                {
                    ProductoId = productoIdTrimmed,
                    ProductoNombre = nombreProducto,
                    AlmacenConStock = null,
                    Disponibles = stockEnAlmacen.CantidadDisponible
                });
            }

            if (!productosProblematicos.Any())
            {
                return PuedeDesmarcar();
            }

            return new ValidarServirJuntoResponse
            {
                PuedeDesmarcar = false,
                DenegadoPorMuestras = true,
                ProductosProblematicos = productosProblematicos,
                Mensaje = ConstruirMensaje(productosProblematicos, modoServicio, almacen, modoCorregidoPorTienda)
            };
        }

        /// <summary>
        /// NestoAPI#394: el mensaje anterior interpolaba la lista de productos DOS veces en la misma
        /// frase ("El producto {lista}... Borre primero el producto {lista}"). Con dos o más muestras
        /// la lista ya lleva comas dentro, así que se leía como UN solo nombre larguísimo: una
        /// comercial lo reportó como fallo cuando en realidad eran dos muestras distintas.
        ///
        /// Ahora la lista aparece UNA vez, al final, con el código delante del nombre (sin el código
        /// hay que buscar la línea a ojo) y con el plural concordado.
        /// </summary>
        /// <remarks>
        /// NestoAPI#638: el mensaje decía que las muestras «se quedarían pendientes» sin decir por qué, y
        /// el motivo es SIEMPRE que no hay stock disponible en el almacén (es lo único que comprueba
        /// el validador). Sin eso parecía que el modo «no funcionaba»: una usuaria lo intentó 8 veces
        /// seguidas (15/10/26). De paso, de usted (mezclaba «Bórralas» con «elija»).
        /// </remarks>
        /// <remarks>
        /// Incidencia 1460 (25/11/26): en una tienda solo vale «Según vaya entrando», así que la salida no puede ser «elija otro modo»:
        /// es borrarlas o pasar el pedido desde Algete. Y si el modo lo cambió la API (el usuario eligió «Todo junto» en tienda,
        /// <paramref name="modoCorregidoPorTienda"/>), el mensaje lo dice en vez de hablar como si hubiera elegido «Según vaya entrando».
        /// </remarks>
        internal static string ConstruirMensaje(List<ProductoSinStockDTO> productosProblematicos, byte? modoServicio = null, string almacen = null,
            bool modoCorregidoPorTienda = false)
        {
            string listaProductos = string.Join(", ",
                productosProblematicos.Select(p => $"{p.ProductoId} {p.ProductoNombre}"));
            bool una = productosProblematicos.Count == 1;
            bool tienda = TextosServirJuntoTienda.EsTienda(almacen);

            if (tienda && modoCorregidoPorTienda)
            {
                return una
                    ? $"{TextosServirJuntoTienda.ArranqueModoCorregido(almacen)} No hay stock en {TextosServirJuntoTienda.Sede(almacen)} de esta muestra " +
                      $"(material promocional) y no puede quedarse pendiente: bórrela del pedido ({listaProductos}) o páselo desde {TextosServirJuntoTienda.Algete()}."
                    : $"{TextosServirJuntoTienda.ArranqueModoCorregido(almacen)} No hay stock en {TextosServirJuntoTienda.Sede(almacen)} de estas muestras " +
                      $"(material promocional) y no pueden quedarse pendientes: bórrelas del pedido ({listaProductos}) o páselo desde {TextosServirJuntoTienda.Algete()}.";
            }

            // NestoAPI#482: se nombra el modo elegido; la casilla «Servir junto» ya no existe en Nesto.
            string destino = Constantes.Pedidos.ModosServicio.NombreDestino(modoServicio);

            // "material promocional" se mantiene en el texto a propósito: explica al usuario POR QUÉ
            // no se puede, y es lo que distingue este mensaje del del validador de regalos.
            // NestoAPI#491: solo se deniega «Según vaya entrando»; los otros modos son la otra salida (en Algete: en tienda no hay otros).
            string alternativa = tienda
                ? $" En {TextosServirJuntoTienda.Sede(almacen)} el pedido solo puede salir «{TextosServirJuntoTienda.ModoTienda()}»: " +
                  $"{(una ? "bórrela" : "bórrelas")} del pedido o páselo desde {TextosServirJuntoTienda.Algete()}."
                : $" O elija otro modo de servicio ({Constantes.Pedidos.ModosServicio.AlternativasSinValidacion()}).";

            string enAlmacen = string.IsNullOrWhiteSpace(almacen) ? "" : $" en {almacen.Trim()}";

            return una
                ? $"No se puede pasar el pedido a «{destino}»: no hay stock{enAlmacen} de esta muestra (material promocional) " +
                  $"y no puede quedarse pendiente. Bórrela del pedido: {listaProductos}.{alternativa}"
                : $"No se puede pasar el pedido a «{destino}»: no hay stock{enAlmacen} de estas muestras (material promocional) " +
                  $"y no pueden quedarse pendientes. Bórrelas del pedido: {listaProductos}.{alternativa}";
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
