using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class GestorReservasStock
    {
        
        public static void Reservar(List<StockProducto> stocks, List<PedidoPicking> candidatos, List<LineaPedidoPicking> todasLasLineas)
        {
            // Excluimos las notas de entrega, porque los ya facturados los metemos como pedido normal
            List<LineaPedidoPicking> lineas = candidatos.Where(c => !c.EsNotaEntrega).SelectMany(l => l.Lineas).OrderBy(l => l.FechaModificacion).ThenBy(l => l.Id).ToList();

            foreach (LineaPedidoPicking linea in todasLasLineas.Where(l=> l.Cantidad != 0 && l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO)) // no pongo > 0 por las bonificaciones
            {
                StockProducto stock = stocks.Where(s => s.Producto == linea.Producto).SingleOrDefault();
                linea.CantidadReservada = linea.Cantidad > stock.StockDisponible ? stock.StockDisponible : linea.Cantidad;
                if (lineas.SingleOrDefault(l => l.Id == linea.Id) != null)
                {
                    lineas.SingleOrDefault(l => l.Id == linea.Id).CantidadReservada = linea.CantidadReservada;
                }
                stock.StockDisponible -= linea.CantidadReservada;
            }

            foreach (LineaPedidoPicking linea in lineas.Where(l => l.Cantidad != 0 && l.TipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE)) 
            {
                linea.CantidadReservada = linea.Cantidad;
            }

        }

        public static void BorrarLineasEntregaFutura(List<PedidoPicking> candidatos, DateTime fechaPicking)
        {
            foreach (PedidoPicking pedido in candidatos)
            {
                pedido.Lineas.RemoveAll(l => l.FechaEntrega > fechaPicking);
            }
        }
    }
}