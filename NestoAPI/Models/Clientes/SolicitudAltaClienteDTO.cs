namespace NestoAPI.Models.Clientes
{
    /// <summary>
    /// Datos que deja un usuario que aún no es cliente para solicitar el alta
    /// desde la tienda online (TiendasNuevaVision#14)
    /// </summary>
    public class SolicitudAltaClienteDTO
    {
        public string Email { get; set; }
        // TiendasNuevaVision#37: el login de la tienda se hace con NIF + email
        public string Nif { get; set; }
        public string Telefono { get; set; }
        public string Pais { get; set; }
        public string CodigoPostal { get; set; }
        public string Comentarios { get; set; }
    }
}
