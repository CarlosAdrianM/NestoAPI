using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure
{
    public interface IGestorStocks
    {
        bool HayStockDisponibleDeTodo(PedidoVentaDTO pedido);
        bool HayStockDisponibleDeTodo(PedidoVentaDTO pedido, string almacen);

        int Stock(string producto);
        int Stock(string producto, string almacen);

        int UnidadesPendientesEntregar(string producto);
        int UnidadesPendientesEntregarAlmacen(string producto, string almacen);

        int UnidadesDisponiblesTodosLosAlmacenes(string producto);
        /// <summary>
        /// NestoAPI#506: el color de la línea en el correo de pedido: "green" (stock en el almacén),
        /// "DeepPink" (hay que traerlo de las tiendas) o "red" (no hay en ningún sitio).
        /// </summary>
        string ColorStock(string producto, string almacen);
        /// <summary>
        /// NestoAPI#515: el mismo color descontando las unidades que se van a pedir, para el pedido que
        /// todavía se está montando (sus líneas aún no están en pendientes de entregar). Con cantidad 0
        /// devuelve lo mismo que la sobrecarga de dos argumentos.
        /// </summary>
        string ColorStock(string producto, string almacen, int cantidad);
    }
}
