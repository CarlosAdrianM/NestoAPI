namespace NestoAPI.Models.Ventas
{
    public class ComparativaVentaDto
    {
        public string Nombre { get; set; }
        public decimal VentaAnnoActual { get; set; }
        public decimal VentaAnnoAnterior { get; set; }
    }
}
