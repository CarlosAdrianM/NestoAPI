using System.Collections.Generic;

namespace NestoAPI.Models.Picking
{
    public interface IRellenadorStocksService
    {
        List<StockProducto> Rellenar(List<PedidoPicking> candidatos);
    }
}
