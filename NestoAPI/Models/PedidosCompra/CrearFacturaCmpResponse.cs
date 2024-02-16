namespace NestoAPI.Models.PedidosCompra
{
    public class CrearFacturaCmpResponse
    {
        public int AsientoFactura { get; set; }
        public int AsientoPago { get; set; }
        public bool Exito { get; set; }
        public int ExtractoProveedorCarteraId { get; set; }
        public int Factura { get; set; }
        public decimal ImporteFactura { get; set; }
        public int Pedido { get; set; }
    }
}