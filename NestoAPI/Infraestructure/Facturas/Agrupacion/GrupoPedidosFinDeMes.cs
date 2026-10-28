using NestoAPI.Models;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#727: los pedidos de fin de mes (FDM, sin P.O.) de un cliente, de TODOS sus contactos, con algún albarán
    /// hasta la fecha de corte. Se reúnen en un único pedido y se factura ese pedido.
    /// </summary>
    public class GrupoPedidosFinDeMes
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public IList<CabPedidoVta> Pedidos { get; set; } = new List<CabPedidoVta>();
    }
}
