using System;
using System.Globalization;
using QRCoder;

namespace NestoAPI.Infraestructure.Verifactu
{
    /// <summary>
    /// NestoAPI#326: genera el QR tributario de una factura EN LOCAL. La URL de validación es la del
    /// servicio ValidarQR de la AEAT (formato de la Orden HAC/1177/2024) y es COMÚN a cualquier
    /// proveedor de Verifactu. Lo específico de cada proveedor —el NIF con el que registra y CÓMO
    /// forma el numserie (Verifacti antepone un prefijo por emisor)— lo aporta quien llama; aquí solo
    /// se ensambla la URL estándar y se pinta el QR. Así ninguna factura sale sin QR aunque falle el
    /// envío al proveedor.
    /// </summary>
    public static class GeneradorQrVerifactu
    {
        // Entorno de PRUEBAS de la AEAT vs PRODUCCIÓN (el QR de una factura registrada en el entorno
        // de pruebas de la AEAT apunta a prewww2; el de producción, al portal real).
        private const string HOST_PRUEBAS = "prewww2.aeat.es";
        private const string HOST_PRODUCCION = "www2.agenciatributaria.gob.es";

        /// <summary>
        /// URL de validación que va codificada en el QR (pura y determinista). Los valores se
        /// codifican con <see cref="Uri.EscapeDataString"/>; para nif/numserie/fecha/importe (solo
        /// alfanuméricos y '_', '-', '.') el resultado es idéntico al que ya devuelve el proveedor.
        /// </summary>
        public static string ConstruirUrlValidacion(string nifEmisor, string numSerie, DateTime fechaExpedicion,
            decimal importeTotal, bool esSandbox)
        {
            string host = esSandbox ? HOST_PRUEBAS : HOST_PRODUCCION;
            string nif = Uri.EscapeDataString(nifEmisor?.Trim() ?? string.Empty);
            string serie = Uri.EscapeDataString(numSerie?.Trim() ?? string.Empty);
            string fecha = fechaExpedicion.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
            string importe = importeTotal.ToString("0.00", CultureInfo.InvariantCulture);
            return $"https://{host}/wlpl/TIKE-CONT/ValidarQR?nif={nif}&numserie={serie}&fecha={fecha}&importe={importe}";
        }

        /// <summary>
        /// PNG del código QR de esa URL. Usa QRCoder (PngByteQRCode: genera los bytes PNG sin
        /// System.Drawing). Nivel de corrección M, que es el que pide la norma. Null si la URL está vacía.
        /// </summary>
        public static byte[] GenerarPngQr(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }
            using (var generador = new QRCodeGenerator())
            using (QRCodeData datos = generador.CreateQrCode(url, QRCodeGenerator.ECCLevel.M))
            {
                return new PngByteQRCode(datos).GetGraphic(10); // 10 px por módulo
            }
        }
    }
}
