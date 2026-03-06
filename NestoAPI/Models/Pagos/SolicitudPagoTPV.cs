namespace NestoAPI.Models.Pagos
{
    public class SolicitudPagoTPV
    {
        public string Empresa { get; set; } = Constantes.Empresas.EMPRESA_POR_DEFECTO;
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public decimal Importe { get; set; }
        public string Descripcion { get; set; }
        public string Correo { get; set; }
        public string UrlOk { get; set; }
        public string UrlKo { get; set; }
        public int? ExtractoClienteId { get; set; }
        public string Documento { get; set; }
        public string Efecto { get; set; }
        public string Vendedor { get; set; }
        public string FormaVenta { get; set; }
        public string Delegacion { get; set; }
        public string TipoApunte { get; set; }
    }
}
