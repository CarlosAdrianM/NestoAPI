using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Picking
{
    /// <summary>
    /// NestoAPI#482, modo 3 «tras reponer de tiendas»: el pedido espera a que las reposiciones
    /// habituales (Reina/Alcobendas → Algete, prdRellenarReposicionStock) traigan el stock que LE
    /// CORRESPONDE, y cuando ya no queda nada que traer para él sale con lo que hay (como el 2).
    ///
    /// «Le corresponde» es la clave (Carlos, 16/09/26): no basta con que haya stock en la tienda;
    /// si un pedido anterior se lo va a llevar, el mío no lo espera. El reparto sigue el criterio
    /// del picking (Fecha_Modificación, luego Nº Orden), que es el que decide quién se lleva el
    /// stock cuando llega a Algete (el SP reparte por Nº Orden; ver #486).
    ///
    /// Por cada línea de producto pendiente del pedido:
    ///   pool          = stock de Algete + stock de tiendas + en camino (todo ANTES de repartir);
    ///   delante       = pendientes de ese producto, en cualquier almacén, anteriores a la línea;
    ///   me corresponde del pool = min(cantidad, max(0, pool − delante));
    ///   me corresponde ahora    = lo que le reservó GestorReservasStock en Algete.
    /// La línea hace esperar si del pool le corresponde MÁS de lo que ya tiene reservado: va a
    /// llegar algo suyo que aún no está (en la tienda, o en camino en cualquier sentido: lo que va
    /// hacia Algete hay que esperarlo, y lo que va hacia una tienda volverá si la tienda no lo
    /// necesita, cosa que ya descuentan sus pendientes en «delante»).
    /// </summary>
    public static class GestorReposicionTiendas
    {
        public static void MarcarEsperas(List<StockProducto> stocksIniciales, List<PedidoPicking> candidatos, List<LineaPedidoPicking> todasLasLineas)
        {
            if (candidatos == null)
            {
                return;
            }
            foreach (PedidoPicking pedido in candidatos.Where(c => !c.EsNotaEntrega
                && c.ModoServicioEfectivo == Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS))
            {
                pedido.EsperaReposicionDeTiendas = pedido.Lineas != null
                    && pedido.Lineas.Any(l => LineaEsperaReposicion(l, stocksIniciales, todasLasLineas));
            }
        }

        internal static bool LineaEsperaReposicion(LineaPedidoPicking linea, List<StockProducto> stocksIniciales, List<LineaPedidoPicking> todasLasLineas)
        {
            if (linea.TipoLinea != Constantes.TiposLineaVenta.PRODUCTO || linea.Cantidad <= 0 || linea.CantidadReservada >= linea.Cantidad)
            {
                return false;
            }
            StockProducto stock = stocksIniciales?.FirstOrDefault(s => s.Producto == linea.Producto);
            if (stock == null)
            {
                return false;
            }

            int pool = stock.StockDisponible + stock.StockTienda + stock.EnCamino;
            int delante = (todasLasLineas ?? new List<LineaPedidoPicking>())
                .Where(o => o.Producto == linea.Producto
                    && o.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO
                    && o.Cantidad > 0
                    && o.Id != linea.Id
                    && EsAnterior(o, linea))
                .Sum(o => o.Cantidad);
            int meCorrespondeDelPool = Math.Min(linea.Cantidad, Math.Max(0, pool - delante));

            return meCorrespondeDelPool > linea.CantidadReservada;
        }

        /// <summary>El orden del picking: Fecha_Modificación y, a igualdad, Nº Orden (GestorReservasStock).</summary>
        private static bool EsAnterior(LineaPedidoPicking otra, LineaPedidoPicking linea)
        {
            return otra.FechaModificacion < linea.FechaModificacion
                || (otra.FechaModificacion == linea.FechaModificacion && otra.Id < linea.Id);
        }
    }
}
