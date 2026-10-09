using System;
using System.Collections.Generic;
using System.Linq;

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
                int cantidadReservadaTienda = 0;
                StockProducto stock = stocks.Where(s => s.Producto == linea.Producto).SingleOrDefault();
                if ((linea.Almacen == Constantes.Productos.ALMACEN_TIENDA || linea.Almacen == Constantes.Almacenes.ALCOBENDAS)&& stock.StockTienda > 0)
                {
                    if (stock.StockTienda >= linea.Cantidad)
                    {
                        linea.CantidadReservada = linea.Cantidad;
                    } else
                    {
                        linea.CantidadReservada = stock.StockTienda;
                    }
                    stock.StockTienda -= linea.CantidadReservada;
                    cantidadReservadaTienda = linea.CantidadReservada;
                } 
                if (linea.CantidadReservada != linea.Cantidad)
                {                    
                    linea.CantidadReservada += linea.Cantidad - linea.CantidadReservada > stock.StockDisponible ? stock.StockDisponible : linea.Cantidad - linea.CantidadReservada;
                    stock.StockDisponible -= linea.CantidadReservada - cantidadReservadaTienda;
                }

                if (lineas.SingleOrDefault(l => l.Id == linea.Id) != null)
                {
                    lineas.SingleOrDefault(l => l.Id == linea.Id).CantidadReservada = linea.CantidadReservada;
                }
            }

            // Si es cuenta contable o línea de texto que no sea pedido especial, asignamos toda la cantidad
            foreach (LineaPedidoPicking linea in lineas.Where(l => l.Cantidad != 0 && (l.TipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE || (l.TipoLinea == Constantes.TiposLineaVenta.TEXTO && !l.EsPedidoEspecial) || l.TipoLinea == Constantes.TiposLineaVenta.INMOVILIZADO)))
            {
                linea.CantidadReservada = linea.Cantidad;
            }

            // Miramos las notas de entrega, para asignar las cuentas contables
            List<LineaPedidoPicking> lineasContables = candidatos.Where(c => c.EsNotaEntrega).SelectMany(l => l.Lineas).OrderBy(l => l.FechaModificacion).ThenBy(l => l.Id).ToList();
            // Si es cuenta contable o línea de texto que no sea pedido especial, asignamos toda la cantidad
            foreach (LineaPedidoPicking lineaContable in lineasContables.Where(l => l.Cantidad != 0 && (l.TipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE || (l.TipoLinea == Constantes.TiposLineaVenta.TEXTO && !l.EsPedidoEspecial))))
            {
                lineaContable.CantidadReservada = lineaContable.Cantidad;
            }
        }

        /// <summary>
        /// NestoAPI#593 (c4), pedido servido por partes (decisión del tablero): el cheque regalo se descuenta en la entrega
        /// con la que lo entregado del pedido SUPERA el mínimo de la campaña (base computable: sin cuentas contables,
        /// ficticios, «PACK 26» ni peluquería). Lo entregado = lo ya albaranado o facturado, más lo que tiene picking de
        /// una pasada anterior, más lo que sale en esta (lo reservado y, en «facturar todo ahora», lo que pasa a Recoger,
        /// que también se factura ahora). Si no lo supera, la línea −1 sale de este picking y se queda en el pedido como
        /// estaba, para la siguiente entrega; si el pedido nunca llega, el cheque no se descuenta. Si lo supera, sale
        /// entera aunque esta entrega sea pequeña: su factura puede quedar en negativo.
        ///
        /// <para>Hasta ahora una línea con cantidad −1 se reservaba siempre en el primer picking (Reservar la da por
        /// servida porque no necesita stock). Va DESPUÉS de BorrarLineasQueNoDebenSalir y de GestorFacturarTodoAhora,
        /// para mirar solo lo que de verdad sale, y ANTES de decidir qué pedidos salen, para que el prepago y los portes
        /// vean la entrega sin el cheque cuando no toca.</para>
        /// </summary>
        public static void ReservarChequesRegalo(List<PedidoPicking> candidatos)
        {
            foreach (PedidoPicking pedido in candidatos.Where(p => !p.EsNotaEntrega && p.ChequeRegalo != null && p.Lineas != null))
            {
                if (!pedido.Lineas.Any(l => l.EsChequeRegalo))
                {
                    continue;
                }
                decimal entregado = pedido.ChequeRegalo.BaseComputableYaEntregada
                    + pedido.Lineas.Where(l => l.ComputaMinimoChequeRegalo && !l.EsChequeRegalo).Sum(BaseComputableQueSale);
                pedido.ChequeRegalo.BaseComputableConEstaEntrega = entregado;
                if (entregado > pedido.ChequeRegalo.MinimoCanje)
                {
                    foreach (LineaPedidoPicking cheque in pedido.Lineas.Where(l => l.EsChequeRegalo))
                    {
                        cheque.CantidadReservada = cheque.Cantidad;
                    }
                    pedido.ChequeRegalo.Retenido = false;
                }
                else
                {
                    _ = pedido.Lineas.RemoveAll(l => l.EsChequeRegalo);
                    pedido.ChequeRegalo.Retenido = true;
                }
            }
        }

        /// <summary>NestoAPI#593: la parte de la base de la línea que sale (y se factura) en esta pasada.</summary>
        internal static decimal BaseComputableQueSale(LineaPedidoPicking linea)
        {
            int unidadesLinea = linea.Cantidad + linea.CantidadRecogida;
            int unidadesQueSalen = linea.CantidadReservada + linea.CantidadARecoger;
            if (unidadesLinea == 0 || unidadesQueSalen == 0)
            {
                return 0;
            }
            return linea.BaseImponible * unidadesQueSalen / unidadesLinea;
        }

        public static void BorrarLineasQueNoDebenSalir(List<PedidoPicking> candidatos, DateTime fechaPicking)
        {
            BorrarLineasEntregaFutura(candidatos, fechaPicking);
            BorrarLineasTextoSinOtroProducto(candidatos);
        }

        private static void BorrarLineasEntregaFutura(List<PedidoPicking> candidatos, DateTime fechaPicking)
        {
            foreach (PedidoPicking pedido in candidatos)
            {
                // NestoAPI#608: se apunta la primera, para poder decir cuándo sale si se queda sin nada
                List<LineaPedidoPicking> futuras = pedido.Lineas.Where(l => l.FechaEntrega > fechaPicking).ToList();
                if (futuras.Count > 0)
                {
                    pedido.PrimeraEntregaFuturaQuitada = futuras.Min(l => l.FechaEntrega);
                }
                pedido.Lineas.RemoveAll(l => l.FechaEntrega > fechaPicking);
            }
        }

        private static void BorrarLineasTextoSinOtroProducto(List<PedidoPicking> candidatos)
        {
            foreach (PedidoPicking pedido in candidatos.Where(c => !c.EsNotaEntrega))
            {
                foreach (LineaPedidoPicking linea in pedido.Lineas.Where(l => l.TipoLinea == Constantes.TiposLineaVenta.TEXTO))
                {
                    linea.Borrar = pedido.Lineas.FirstOrDefault(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && l.CantidadReservada > 0) == null;
                }
                pedido.Lineas.RemoveAll(l => l.Borrar);
            }
        }
    }
}