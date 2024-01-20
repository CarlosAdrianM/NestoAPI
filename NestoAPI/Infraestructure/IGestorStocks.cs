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
    }
}
