using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class GestorStocksPicking
    {
        private PedidoPicking pedido;
        public GestorStocksPicking(PedidoPicking pedido)
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

        /// <summary>
        /// NestoAPI#529: lo que no tiene stock en esta pasada son solo regalos (líneas de producto
        /// a base 0: Ganavisiones, regalo por importe, material promocional, unidades gratis de
        /// una oferta). Servir lo demás obligaría a mandar después el regalo solo, en un envío de
        /// 0 €. Falso si no queda nada pendiente.
        /// </summary>
        public bool LoPendienteSonSoloRegalos()
        {
            if (pedido.EsNotaEntrega || pedido.Lineas == null)
            {
                return false;
            }
            List<LineaPedidoPicking> pendientes = pedido.Lineas
                .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && l.Cantidad > l.CantidadReservada)
                .ToList();
            return pendientes.Any() && pendientes.All(l => l.BaseImponible == 0);
        }

        public bool HayStockDeAlgo()
        {
            bool lineaEncontrada;
            if (pedido.EsNotaEntrega)
            {
                lineaEncontrada = pedido.Lineas.Any(l => l.CantidadReservada > 0 || !pedido.EsProductoYaFacturado || (l.TipoLinea == Constantes.TiposLineaVenta.TEXTO && l.Cantidad == 0));
            } else
            {
                lineaEncontrada = pedido.Lineas.Any(
                    l => (l.CantidadReservada > 0 || (l.CantidadRecogida > 0 && l.Cantidad == l.CantidadReservada) || (l.Cantidad == l.CantidadReservada && l.TipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE)) && 
                    (l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO || l.TipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE || l.TipoLinea == Constantes.TiposLineaVenta.INMOVILIZADO)
                );
            }
            return lineaEncontrada;
        }

        public bool TodoLoQueTieneStockEsSobrePedido()
        {
            bool hayLineas = pedido.Lineas.Any(l => !l.EsSobrePedido && l.CantidadReservada > 0 || 
                (l.TipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE && l.CantidadReservada < 0));
            return !hayLineas;
        }

        
    }
}