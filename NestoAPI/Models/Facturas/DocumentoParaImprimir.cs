namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Contiene la información necesaria para imprimir un documento (factura o albarán).
    /// El servidor genera los bytes del PDF y especifica cómo imprimirlo.
    /// El cliente WPF usa PdfiumViewer para enviarlo a la impresora.
    /// </summary>
    public class DocumentoParaImprimir
    {
        /// <summary>
        /// Bytes del documento PDF listo para imprimir
        /// </summary>
        public byte[] BytesPDF { get; set; }

        /// <summary>
        /// Número de copias a imprimir
        /// </summary>
        public int NumeroCopias { get; set; }

        /// <summary>
        /// Bandeja de impresión a utilizar
        /// </summary>
        public string Bandeja { get; set; }
    }
}
