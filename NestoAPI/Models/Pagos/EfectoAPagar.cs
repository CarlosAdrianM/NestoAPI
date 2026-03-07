namespace NestoAPI.Models.Pagos
{
    public class EfectoAPagar
    {
        public int ExtractoClienteId { get; set; }
        public decimal Importe { get; set; }
        public string Documento { get; set; }
        public string Efecto { get; set; }
        public string Contacto { get; set; }
        public string Vendedor { get; set; }
        public string FormaVenta { get; set; }
        public string Delegacion { get; set; }
        public string TipoApunte { get; set; }
    }
}
