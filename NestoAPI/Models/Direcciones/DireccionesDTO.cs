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

        /// <summary>Nesto#436: nombre del país de la dirección ("Italia") tal y como lo da Google.</summary>
        public string Pais { get; set; }

        /// <summary>Nesto#436: código ISO 3166-1 alpha-2 del país ("IT"), para la ficha del cliente.</summary>
        public string PaisIso { get; set; }

        /// <summary>
        /// NestoAPI#826 (sugerencia 1058): calle y número juntos en el formato del país de la dirección,
        /// para el campo «Dirección» del alta. España: «Calle Mayor, 12»; Francia y los países donde el
        /// número va delante: «12 Rue de la Paix»; el resto: «Via Roma 1». Sin número, solo la calle.
        /// </summary>
        public string DireccionCalleNumero { get; set; }
    }
}
