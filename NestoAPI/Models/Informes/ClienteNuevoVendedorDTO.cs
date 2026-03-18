namespace NestoAPI.Models.Informes
{
    public class ClienteNuevoVendedorDTO
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Telefono { get; set; }
        public short Estado { get; set; }
        public string Origen { get; set; }
    }
}
