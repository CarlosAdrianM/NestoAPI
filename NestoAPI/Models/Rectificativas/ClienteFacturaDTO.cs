namespace NestoAPI.Models.Rectificativas
{
    /// <summary>
    /// DTO para devolver la informacion del cliente de una factura.
    /// Issue #85
    /// </summary>
    public class ClienteFacturaDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
    }
}
