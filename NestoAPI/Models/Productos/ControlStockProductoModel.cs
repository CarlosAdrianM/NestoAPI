using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Productos
{
    public class ControlStockProductoModel : ControlStockBase
    {
        public ControlStockProductoModel()
        {
            ControlesStocksAlmacen = new List<ControlStockAlmacenModel>();
        }
        public string ProductoId { get; set; }        
        public decimal PuntoPedidoCalculado => ConsumoMedioDiario * (DiasStockSeguridad + DiasReaprovisionamiento);
        public int StockMinimoActual { get; set; }
        public int StockMinimoCalculado
        {
            get
            {
                if (PuntoPedidoCalculado < 1)
                {
                    return (int)Math.Ceiling(PuntoPedidoCalculado);
                }
                return (int)Math.Round(PuntoPedidoCalculado, 0, MidpointRounding.AwayFromZero);
            }
        }
        public ICollection<ControlStockAlmacenModel> ControlesStocksAlmacen { get; set; }        
    }
}