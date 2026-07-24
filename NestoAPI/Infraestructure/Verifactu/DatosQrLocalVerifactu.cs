namespace NestoAPI.Infraestructure.Verifactu
{
    /// <summary>
    /// NestoAPI#326: QR tributario generado EN LOCAL (sin llamar al proveedor): la URL de validación
    /// de la AEAT y la imagen PNG lista para pintar en la factura.
    /// </summary>
    public class DatosQrLocalVerifactu
    {
        /// <summary>URL del servicio ValidarQR de la AEAT que va codificada en el QR.</summary>
        public string Url { get; set; }

        /// <summary>Imagen PNG del código QR.</summary>
        public byte[] ImagenPngQr { get; set; }
    }
}
