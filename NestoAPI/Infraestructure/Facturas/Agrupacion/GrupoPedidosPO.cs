using NestoAPI.Models;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#195 (Fase 2): representa un conjunto de pedidos de venta que comparten
    /// <c>(Empresa, Nº_Cliente, SuPedido)</c> con <c>MantenerJunto = true</c> y que están
    /// listos para agruparse en una única factura (todos sus pedidos tienen las líneas en
    /// albarán). Cada pedido puede tener un contacto/dirección de entrega distinto: el
    /// sentido de la agrupación por PO es "un único pedido del cliente (P.O.), varias
    /// direcciones de entrega, una sola factura".
    /// </summary>
    public class GrupoPedidosPO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string SuPedido { get; set; }
        public IList<CabPedidoVta> Pedidos { get; set; } = new List<CabPedidoVta>();
    }
}
