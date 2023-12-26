using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class RellenadorStocksService : IRellenadorStocksService
    {
        // Esta clase, al ser un servicio que lee de base de datos, no está testada
        // La probaremos cuando tengamos creada GestorPicking, mientras tanto usaremos stubs
        private NVEntities db = new NVEntities();
        
        public List<StockProducto> Rellenar(List<PedidoPicking> candidatos)
        {
            List<LineaPedidoPicking> lineas = candidatos.SelectMany(l => l.Lineas).ToList();
            List<string> productos = lineas.Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO).GroupBy(l => l.Producto).Select(l => l.Key).ToList();
            List<StockProducto> stocks = productos.Select(s => new StockProducto
            {
                Producto = s,
                StockDisponible = db.ExtractosProducto.Where(e => e.Número == s && e.Almacén == Constantes.Productos.ALMACEN_POR_DEFECTO).Select(l => (int)l.Cantidad).DefaultIfEmpty(0).Sum(),
                StockTienda = db.ExtractosProducto.Where(e => e.Número == s && (e.Almacén == Constantes.Productos.ALMACEN_TIENDA || e.Almacén == Constantes.Almacenes.ALCOBENDAS)).Select(l => (int)l.Cantidad).DefaultIfEmpty(0).Sum()
            }).ToList();
            return stocks;
        }
    }
}