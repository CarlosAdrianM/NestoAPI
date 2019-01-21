using NestoAPI.Models;

namespace NestoAPI.Infraestructure
{
    public interface IGestorStocks
    {
        bool HayStockDisponibleDeTodo(PedidoVentaDTO pedido);

        int Stock(string producto, string almacen);

        int UnidadesPendientesEntregarAlmacen(string producto, string almacen);

        int UnidadesDisponiblesTodosLosAlmacenes(string producto);
    }
}
