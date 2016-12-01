﻿using System;
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

            LineaPedidoPicking linea = pedido.Lineas.FirstOrDefault(l => l.Cantidad > l.CantidadReservada && l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO);
            return linea == null;
        }

        public bool HayStockDeAlgo()
        {
            LineaPedidoPicking linea = pedido.Lineas.FirstOrDefault(l => l.CantidadReservada > 0 && l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO);
            return linea != null;
        }

        public bool TodoLoQueTieneStockEsSobrePedido()
        {
            LineaPedidoPicking linea = pedido.Lineas.FirstOrDefault(l => !l.EsSobrePedido && l.Cantidad == l.CantidadReservada);
            return linea == null;
        }

        
    }
}