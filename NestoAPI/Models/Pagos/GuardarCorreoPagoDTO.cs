namespace NestoAPI.Models.Pagos
{
    /// <summary>
    /// NestoAPI#197: petición del cliente final desde la página /pago/{token} para recibir el
    /// justificante por correo cuando el enlace se generó sin él.
    /// </summary>
    public class GuardarCorreoPagoDTO
    {
        public string Correo { get; set; }
        // Solo tiene efecto si el cliente no tiene ya una persona de contacto con cargo 22.
        public bool DeseaFacturasElectronicas { get; set; }
    }

    public class RespuestaGuardarCorreoPago
    {
        public bool CorreoGuardado { get; set; }
        public bool FacturasElectronicas { get; set; }
    }
}
