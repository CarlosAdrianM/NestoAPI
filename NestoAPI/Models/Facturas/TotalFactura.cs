namespace NestoAPI.Models.Facturas
{
    public class TotalFactura
    {
        public decimal BaseImponible { get; set; }
        public decimal PorcentajeIVA { get; set; }
        public decimal ImporteIVA { get; set; }
        public decimal PorcentajeRecargoEquivalencia { get; set; }
        public decimal ImporteRecargoEquivalencia { get; set; }
    }
}