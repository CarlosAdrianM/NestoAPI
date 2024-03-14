namespace NestoAPI.Models.PedidosVenta
{
    public class PrepagoDTO
    {
        public string ConceptoAdicional { get; set; }
        public string CuentaContable { get; set; }        
        public string Factura { get; set; }
        public decimal Importe { get; set; }        
        public int Pedido { get; set; }
    }
}