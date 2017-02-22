using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class GestorStocks
    {
        private PedidoPicking pedido;
        public GestorStocks(PedidoPicking pedido)
        {
            this.pedido = pedido;
        }

        public bool HayStockDeTodo()
        {
            if (pedido.EsNotaEntrega)
            {
                return true;
            }

            LineaPedidoPicking linea = pedido.Lineas.FirstOrDefault(l => (l.Cantidad > l.CantidadReservada && l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO) || (l.EsPedidoEspecial && l.TipoLinea == Constantes.TiposLineaVenta.TEXTO));
            return linea == null;
        }

        public bool HayStockDeAlgo()
        {
            LineaPedidoPicking linea;
            if (pedido.EsNotaEntrega)
            {
                linea = pedido.Lineas.FirstOrDefault(l => l.CantidadReservada > 0 || !pedido.EsProductoYaFacturado || (l.TipoLinea == Constantes.TiposLineaVenta.TEXTO && l.Cantidad == 0));
            } else
            {
                linea = pedido.Lineas.FirstOrDefault(l => l.CantidadReservada > 0 && (l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO || l.TipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE || l.TipoLinea == Constantes.TiposLineaVenta.INMOVILIZADO));
            }
            return linea != null;
        }

        public bool TodoLoQueTieneStockEsSobrePedido()
        {
            LineaPedidoPicking linea = pedido.Lineas.FirstOrDefault(l => !l.EsSobrePedido && l.CantidadReservada > 0);
            return linea == null;
        }

        
    }
}