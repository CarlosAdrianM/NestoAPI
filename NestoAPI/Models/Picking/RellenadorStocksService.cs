using NestoAPI.Infraestructure.Stocks;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Picking
{
    public class RellenadorStocksService : IRellenadorStocksService
    {
        // Esta clase, al ser un servicio que lee de base de datos, no está testada
        // La probaremos cuando tengamos creada GestorPicking, mientras tanto usaremos stubs
        private NVEntities db = new NVEntities();
        private readonly ILectorRepartoStock lectorRepartoStock;

        public RellenadorStocksService()
        {
            lectorRepartoStock = new LectorRepartoStock(db);
        }

        public List<StockProducto> Rellenar(List<PedidoPicking> candidatos)
        {
            List<LineaPedidoPicking> lineas = candidatos.SelectMany(l => l.Lineas).ToList();
            List<string> productos = lineas.Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO).GroupBy(l => l.Producto).Select(l => l.Key).ToList();
            // NestoAPI#730: la MISMA lectura de stock que el reparto de un solo pedido (fecha de entrega, avisos de riesgo)
            return lectorRepartoStock.LeerStocks(productos);
        }
    }
}
