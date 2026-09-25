using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Picking
{
    /// <summary>
    /// NestoAPI#542: modo de facturación «todo ahora, lo pendiente se entrega después». El pedido se factura
    /// entero con el primer albarán, así que lo que en esta pasada no tiene stock no se queda pendiente (−1) ni
    /// parte la línea: pasa a <c>Recoger</c>, la línea entra en el picking con las unidades que sí hay (0 si no
    /// hay ninguna) y prdCrearAlbaránVta hace el resto (factura la cantidad completa, mueve solo el stock
    /// entregado y marca YaFacturado). Lo pendiente lo recoge después la nota de entrega automática
    /// (CreadorNotaEntregaPendiente).
    ///
    /// <para>Qué sale ahora lo sigue decidiendo el modo de SERVICIO (Carlos, 25/09/26): con «todo junto» (o un
    /// modo 4 que ya tuvo su primera entrega), si falta algo no sale nada y TODO va a Recoger; con los modos
    /// parciales sale lo que hay. «Tras reponer de tiendas» espera primero la reposición, como siempre. Las
    /// retenciones por prepago y por cierre del cliente siguen mandando: el pedido se queda como está.</para>
    ///
    /// <para>Se aplica ANTES de decidir qué pedidos salen (saleEnPicking, HayStockDeAlgo, portes), para que
    /// esas reglas vean el pedido ya convertido: con Cantidad == CantidadReservada en todas las líneas,
    /// HayStockDeTodo es cierto, y HayStockDeAlgo ya admitía «todo a recoger» (CantidadRecogida > 0 con
    /// Cantidad == CantidadReservada), que es como se hacía a mano desde el Nesto viejo.</para>
    /// </summary>
    public static class GestorFacturarTodoAhora
    {
        public static void Aplicar(List<PedidoPicking> candidatos)
        {
            foreach (PedidoPicking pedido in candidatos.Where(SeConvierte))
            {
                bool saleTodoOnada = pedido.ExigeStockDeTodo();
                bool faltaAlgo = !new GestorStocksPicking(pedido).HayStockDeTodo();
                if (saleTodoOnada && faltaAlgo)
                {
                    TodoARecoger(pedido);
                }
                else
                {
                    LoQueFaltaARecoger(pedido);
                }
            }
        }

        /// <summary>Solo los pedidos normales en modo 3 que no están retenidos por otra causa.</summary>
        internal static bool SeConvierte(PedidoPicking pedido)
        {
            if (!pedido.FacturaTodoAhora || pedido.EsNotaEntrega || pedido.Borrar || pedido.Lineas == null || pedido.Lineas.Count == 0)
            {
                return false;
            }
            if (pedido.ModoServicioEfectivo == Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS && pedido.EsperaReposicionDeTiendas)
            {
                return false;
            }
            return pedido.CubiertoPorPrepago();
        }

        private static IEnumerable<LineaPedidoPicking> Productos(PedidoPicking pedido)
            => pedido.Lineas.Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && l.Cantidad > 0);

        private static void LoQueFaltaARecoger(PedidoPicking pedido)
        {
            foreach (LineaPedidoPicking linea in Productos(pedido).Where(l => l.CantidadReservada < l.Cantidad))
            {
                Recoger(linea, linea.Cantidad - linea.CantidadReservada);
            }
        }

        /// <summary>«Todo junto» sin stock de todo: se factura ya, pero se entrega todo junto más adelante. Lo
        /// reservado en esta pasada se suelta (como cuando un todo junto no sale: se queda sin usar hasta la
        /// siguiente).</summary>
        private static void TodoARecoger(PedidoPicking pedido)
        {
            foreach (LineaPedidoPicking linea in Productos(pedido))
            {
                linea.CantidadReservada = 0;
                Recoger(linea, linea.Cantidad);
            }
        }

        private static void Recoger(LineaPedidoPicking linea, int unidades)
        {
            linea.CantidadARecoger += unidades;
            linea.CantidadRecogida += unidades;
            linea.Cantidad -= unidades;
        }
    }
}
