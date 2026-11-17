using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;

namespace NestoAPI.Models.Picking
{
    public interface IRellenadorPrepagosService
    {
        List<PrepagoDTO> Prepagos(int pedido);
        List<ExtractoClienteDTO> ExtractosPendientes(int pedido);
        string CorreoUsuario(string usuario);
        /// <summary>Novedades 1302: los OTROS pedidos PRE del cliente de este pedido que están en un picking anterior y
        /// aún sin facturar, con su importe en picking (con IVA) y sus prepagos sin factura. Una consulta.</summary>
        List<PedidoPrepagoEnPickingDTO> PedidosPrepagoEnPicking(int pedido);
    }
}
