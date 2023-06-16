namespace NestoAPI.Models.PedidosVenta
{
    public class PrepagoDTO
    {
        public decimal Importe { get; set; }
        public string Factura { get; set; }
        public string CuentaContable { get; set; }
        public string ConceptoAdicional { get; set; }
    }
}