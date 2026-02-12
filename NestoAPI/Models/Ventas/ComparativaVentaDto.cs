namespace NestoAPI.Models.Ventas
{
    public class ComparativaVentaDto
    {
        public string Nombre { get; set; }
        public decimal VentaAnnoActual { get; set; }
        public decimal VentaAnnoAnterior { get; set; }
        public int UnidadesAnnoActual { get; set; }
        public int UnidadesAnnoAnterior { get; set; }
    }
}
