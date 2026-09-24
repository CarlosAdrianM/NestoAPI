using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    /// <summary>
    /// NestoAPI#529: no se mete un regalo que no tiene stock. Regalo = línea de producto a base 0
    /// sin nº de oferta (Ganavisión, regalo por importe de pedido, material promocional); las
    /// unidades gratis de una oferta las decide la oferta. Un regalo sin stock acababa saliendo
    /// solo, en un envío de 0 € (pedido 926923), o dejando más pendientes que stock (40144, #528).
    ///
    /// <para>Es un rechazo DURO (AutorizadaDenegadaExpresamente): ningún validador de aceptación lo
    /// anula. Hasta ahora el stock solo lo miraba ValidadorGanavisiones, y solo cuando otro
    /// validador de denegación había señalado antes el producto.</para>
    ///
    /// <para>Solo cuentan las unidades NUEVAS: líneas nuevas, líneas a las que se cambia el
    /// producto y lo que sube la cantidad de una línea guardada. Lo ya guardado se respeta, como
    /// en ValidadorGanavisiones (#228). El stock es el mismo que usa ese validador
    /// (BuscarStockDisponibleTotal).</para>
    /// </summary>
    public class ValidadorRegaloSinStock : IValidadorDenegacion
    {
        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, IServicioPrecios servicio)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion { ValidacionSuperada = true };
            if (pedido?.Lineas == null)
            {
                return respuesta;
            }

            IEnumerable<IGrouping<string, LineaPedidoVentaDTO>> regalosPorProducto = pedido.Lineas
                .Where(EsRegalo)
                .GroupBy(l => l.Producto.Trim());

            foreach (IGrouping<string, LineaPedidoVentaDTO> regalos in regalosPorProducto)
            {
                int unidadesNuevas = regalos.Sum(UnidadesNuevas);
                if (unidadesNuevas <= 0 || servicio.BuscarProducto(regalos.Key)?.Ficticio == true)
                {
                    // Un producto ficticio no lleva stock: exigírselo lo bloquearía siempre
                    continue;
                }

                int disponible = servicio.BuscarStockDisponibleTotal(regalos.Key);
                if (unidadesNuevas > disponible)
                {
                    respuesta.ValidacionSuperada = false;
                    respuesta.AutorizadaDenegadaExpresamente = true;
                    respuesta.ProductoId = regalos.Key;
                    respuesta.Motivo = disponible <= 0
                        ? $"No se puede regalar el producto {regalos.Key}: no hay stock. Elige otro regalo."
                        : $"No se pueden regalar {unidadesNuevas} unidades del producto {regalos.Key}: solo hay {disponible} disponibles.";
                    return respuesta;
                }
            }

            return respuesta;
        }

        internal static bool EsRegalo(LineaPedidoVentaDTO linea)
        {
            return linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO
                && !string.IsNullOrWhiteSpace(linea.Producto)
                && linea.Cantidad > 0
                && linea.BaseImponible == 0
                && (linea.oferta == null || linea.oferta == 0);
        }

        internal static int UnidadesNuevas(LineaPedidoVentaDTO linea)
        {
            if (linea.EsLineaNueva || linea.CambioProducto)
            {
                return linea.Cantidad;
            }
            return linea.CantidadAnterior.HasValue ? Math.Max(0, linea.Cantidad - linea.CantidadAnterior.Value) : 0;
        }
    }
}
