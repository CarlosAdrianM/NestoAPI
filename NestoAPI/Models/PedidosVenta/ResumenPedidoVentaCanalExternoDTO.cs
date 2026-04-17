using System;

namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// Resumen de un pedido de venta originado en un canal externo (Amazon, TiendaOnline…).
    /// Pensado para el cuadre Canales Externos (Nesto#349): permite emparejar pedidos del
    /// canal con los que hemos bajado a Nesto. Devuelve solo los campos necesarios para el
    /// matching y la presentación.
    /// </summary>
    public class ResumenPedidoVentaCanalExternoDTO
    {
        public string Empresa { get; set; }
        public int Numero { get; set; }
        public DateTime Fecha { get; set; }
        public string Cliente { get; set; }

        /// <summary>
        /// Identificador del pedido en el canal externo (p. ej. AmazonOrderId con formato
        /// 123-4567890-1234567). Se extrae del campo Comentarios del pedido cuando ese
        /// patrón aparece; null si no se reconoce.
        /// </summary>
        public string CanalOrderId { get; set; }
    }
}
