using NestoAPI.Models;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#195 (Fase 2): núcleo común de agrupación de pedidos de venta.
    /// Mueve las líneas de N pedidos origen a un único pedido destino y mantiene
    /// coherentes las tablas satélite (PedidosEspeciales, Ubicaciones), sin tocar el
    /// SP de facturación. Es agnóstico al criterio de agrupación (PO, FDM, ...): la
    /// estrategia decide qué pedidos agrupar y cuál es el destino; el motor solo ejecuta
    /// el movimiento. Pensado para que la agrupación FDM pueda migrar a él en el futuro.
    /// </summary>
    public interface IMotorAgrupacionPedidos
    {
        /// <summary>
        /// Mueve todas las líneas en albarán de los pedidos <paramref name="origenes"/>
        /// (excepto el propio destino) al pedido <paramref name="destino"/>, repunta sus
        /// PedidosEspeciales y Ubicaciones, y marca el destino como agrupado.
        /// No persiste: el llamante hace SaveChanges. Devuelve el pedido destino.
        /// </summary>
        CabPedidoVta Agrupar(IEnumerable<CabPedidoVta> origenes, CabPedidoVta destino);
    }
}
