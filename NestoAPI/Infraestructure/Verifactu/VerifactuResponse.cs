namespace NestoAPI.Infraestructure.Verifactu
{
    /// <summary>
    /// Respuesta genérica de un proveedor de Verifactu tras enviar una factura.
    /// </summary>
    public class VerifactuResponse
    {
        /// <summary>
        /// Indica si el envío fue exitoso
        /// </summary>
        public bool Exitoso { get; set; }

        /// <summary>
        /// UUID asignado por el proveedor a esta factura
        /// </summary>
        public string Uuid { get; set; }

        /// <summary>
        /// Estado de la factura en el sistema Verifactu
        /// </summary>
        public string Estado { get; set; }

        /// <summary>
        /// URL para consultar/verificar la factura
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Código QR en Base64 para imprimir en la factura
        /// </summary>
        public string QrBase64 { get; set; }

        /// <summary>
        /// Huella digital SHA-256 de la factura
        /// </summary>
        public string Huella { get; set; }

        /// <summary>
        /// Mensaje de error si Exitoso = false
        /// </summary>
        public string MensajeError { get; set; }

        /// <summary>
        /// Código de error del proveedor
        /// </summary>
        public string CodigoError { get; set; }
    }
}
