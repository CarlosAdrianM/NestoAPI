namespace NestoAPI.Models
{
    /// <summary>
    /// NestoAPI#447: una persona de contacto del centro, tal como la ve el titular (cargo 22)
    /// desde la app de clientes para gestionar quién ve qué.
    /// </summary>
    public class PersonaContactoCentroDTO
    {
        public string Contacto { get; set; }
        public string Numero { get; set; }
        public string Nombre { get; set; }
        public string CorreoElectronico { get; set; }
        public short Cargo { get; set; }

        /// <summary>El texto del nivel ("Solo pide, sin precios"...), compuesto por el servidor.</summary>
        public string Nivel { get; set; }

        /// <summary>Cargo 22: ve facturas y gestiona a las demás.</summary>
        public bool EsTitular { get; set; }

        /// <summary>La persona con el correo del que ha iniciado sesión.</summary>
        public bool EsYo { get; set; }

        /// <summary>Solo las personas con correo pueden entrar en la app; las demás se enseñan sin selector.</summary>
        public bool TieneCorreo { get; set; }
    }

    public class CambioCargoPersonaContactoRequest
    {
        public short Cargo { get; set; }
    }
}
