namespace NestoAPI.Models.Informes
{
    public class ResumenVentasDTO
    {
        public string Grupo { get; set; }
        public string Vendedor { get; set; }
        public string NombreVendedor { get; set; }
        public decimal VtaNV { get; set; }
        public decimal VtaCV { get; set; }
        public decimal VtaVC { get; set; }
        public decimal VtaUL { get; set; }
        public decimal VtaTotal { get; set; }
    }
}
