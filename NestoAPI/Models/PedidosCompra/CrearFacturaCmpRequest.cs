using System;

namespace NestoAPI.Models.PedidosCompra
{
    public class CrearFacturaCmpRequest
    {
        public PedidoCompraDTO Pedido { get; set; }
        public bool CrearPago { get; set; }
        public string ContraPartidaPago { get; set; }
        public string Documento { get; set; } // para poner datos que nos interese guardar
        /// <summary>
        /// NestoAPI#668: opcional. Si viene, el pedido, el albarán y la factura van con esta fecha y el pago
        /// se queda con la del pedido (la del movimiento del banco). Sin ella, todo como siempre.
        /// </summary>
        public DateTime? FechaFactura { get; set; }
    }
}