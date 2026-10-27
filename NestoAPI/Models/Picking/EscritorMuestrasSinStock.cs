using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Picking
{
    /// <summary>
    /// 27/10/26 (Carlos, caso David): escribe en LinPedidoVta lo que <see cref="GestorMuestrasSinStock"/> decidió en memoria
    /// para los pedidos que SALEN: la línea de la muestra sin stock se borra (como al quitarla a mano desde el pedido) o, si
    /// una parte sí tenía stock, se reduce a esa parte con los importes recalculados (el mismo DividirLinea del picking, sin
    /// quedarse con la línea pendiente). Lo reservado no cambia: lo que se quita no tenía stock.
    ///
    /// <para>Rastro: una fila en Modificaciones por línea quitada, con el usuario <see cref="GestorMuestrasSinStock.USUARIO_AUDITORIA"/>,
    /// y una entrada en ELMAH por pedido. Un fallo al escribir el rastro no rompe el picking.</para>
    /// </summary>
    public static class EscritorMuestrasSinStock
    {
        public const string TABLA_MODIFICACIONES = "LinPedidoVta";
        public const string OPERACION = "Picking: muestra sin stock quitada del pedido";

        public static void Escribir(NVEntities db, IEnumerable<PedidoPicking> pedidosQueSalen)
        {
            if (db == null || pedidosQueSalen == null)
            {
                return;
            }
            PedidosVentaController pedidosCtrl = null;
            foreach (PedidoPicking pedido in pedidosQueSalen.Where(p => p != null && !p.Borrar && p.MuestrasQuitadas != null && p.MuestrasQuitadas.Any()))
            {
                foreach (MuestraQuitadaPicking quitada in pedido.MuestrasQuitadas)
                {
                    LinPedidoVta linea = db.LinPedidoVtas.SingleOrDefault(l => l.Nº_Orden == quitada.IdLinea);
                    if (linea == null)
                    {
                        continue;
                    }
                    if (quitada.UnidadesQueSalen <= 0)
                    {
                        _ = db.LinPedidoVtas.Remove(linea);
                    }
                    else
                    {
                        // La cantidad de LinPedidoVta incluye lo que ya está a Recoger (LineaPedidoPicking.Cantidad lo descuenta)
                        short cantidadQueQueda = (short)(quitada.UnidadesQueSalen + linea.Recoger);
                        pedidosCtrl = pedidosCtrl ?? new PedidosVentaController();
                        LinPedidoVta parteSinStock = pedidosCtrl.dividirLinea(db, linea, cantidadQueQueda);
                        if (parteSinStock != null)
                        {
                            // DividirLinea la ha añadido al contexto como la parte pendiente; aquí esa parte no se queda
                            _ = db.LinPedidoVtas.Remove(parteSinStock);
                        }
                    }
                    _ = db.Modificaciones.Add(Modificacion(pedido, quitada, DateTime.Now));
                }
                ElmahHelper.Log(new Exception(
                    $"[Picking muestras] Pedido {pedido.Empresa?.Trim()}/{pedido.Id} (cliente {pedido.Cliente?.Trim()}, usuario {pedido.Usuario?.Trim()}): " +
                    $"sin stock de la(s) muestra(s) {GestorMuestrasSinStock.Describir(pedido.MuestrasQuitadas)}; se quitan del pedido y sale con el resto " +
                    $"(regla de Carlos 27/10/26, interruptor {GestorMuestrasSinStock.CLAVE})."), GestorMuestrasSinStock.USUARIO_AUDITORIA);
            }
        }

        /// <summary>La fila de Modificaciones de una muestra quitada (JSON antes/después, como el resto de rastros del pedido).</summary>
        internal static Modificacion Modificacion(PedidoPicking pedido, MuestraQuitadaPicking quitada, DateTime fecha)
        {
            return new Modificacion
            {
                Tabla = TABLA_MODIFICACIONES,
                Anterior = JsonConvert.SerializeObject(new
                {
                    Empresa = pedido.Empresa?.Trim(),
                    Pedido = pedido.Id,
                    NumeroOrden = quitada.IdLinea,
                    Producto = quitada.Producto,
                    Cantidad = quitada.UnidadesQuitadas + quitada.UnidadesQueSalen
                }),
                Nuevo = JsonConvert.SerializeObject(new
                {
                    Operacion = OPERACION,
                    Cantidad = quitada.UnidadesQueSalen,
                    LineaBorrada = quitada.UnidadesQueSalen <= 0,
                    Motivo = $"Sin stock de {quitada.UnidadesQuitadas} ud(s) de la muestra {quitada.Producto}; el resto del pedido sale " +
                        $"(Carlos 27/10/26, {GestorMuestrasSinStock.CLAVE})"
                }),
                Usuario = GestorMuestrasSinStock.USUARIO_AUDITORIA,
                Fecha = fecha
            };
        }
    }
}
