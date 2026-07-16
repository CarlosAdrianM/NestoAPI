namespace NestoAPI.Models.Direcciones
{
    /// <summary>
    /// NestoAPI#306: una sugerencia del autocompletado de direcciones (Google Places).
    /// </summary>
    public class SugerenciaDireccionDTO
    {
        public string Descripcion { get; set; }
        public string PlaceId { get; set; }
    }

    /// <summary>
    /// NestoAPI#306: el detalle de la dirección seleccionada, con los componentes ya troceados
    /// (lo que necesita el alta de clientes: calle, número y código postal).
    /// </summary>
    public class DireccionDetalleDTO
    {
        public string Calle { get; set; }
        public string Numero { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        public string DireccionFormateada { get; set; }
    }
}
