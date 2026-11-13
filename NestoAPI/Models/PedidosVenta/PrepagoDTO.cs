namespace NestoAPI.Models.PedidosVenta
{
    public class PrepagoDTO
    {
        public string ConceptoAdicional { get; set; }
        public string CuentaContable { get; set; }        
        public string Factura { get; set; }
        public decimal Importe { get; set; }        
        public int Pedido { get; set; }
        /// <summary>
        /// NestoAPI#860: el día real del cobro (Prepagos.FechaCobro, fuera del EDMX). Opcional: null = como hoy (al
        /// contabilizar, la fecha de la factura) y, al modificar el pedido, no se toca lo guardado. Con hora, solo cuenta el
        /// día. Posterior a hoy → 400.
        /// </summary>
        public System.DateTime? FechaCobro { get; set; }
    }
}