namespace NestoAPI.Models.Pagos
{
    public class SolicitudPagoTPV
    {
        public string Empresa { get; set; } = Constantes.Empresas.EMPRESA_POR_DEFECTO;
        public string Cliente { get; set; }
        public decimal Importe { get; set; }
        public string Descripcion { get; set; }
        public string Correo { get; set; }
    }
}
