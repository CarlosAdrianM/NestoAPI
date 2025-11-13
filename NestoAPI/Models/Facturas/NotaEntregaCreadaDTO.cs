namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Representa una nota de entrega creada durante el proceso de facturación de rutas.
    /// Las notas de entrega documentan entregas de productos que pueden estar ya facturados o pendientes de facturación.
    /// Hereda de DocumentoImprimibleDTO para soportar impresión de PDFs.
    /// </summary>
    public class NotaEntregaCreadaDTO : DocumentoImprimibleDTO
    {
        /// <summary>
        /// Número de líneas procesadas en la nota de entrega
        /// </summary>
        public int NumeroLineas { get; set; }

        /// <summary>
        /// Indica si alguna línea era YaFacturado=true (requirió dar de baja stock)
        /// </summary>
        public bool TeniaLineasYaFacturadas { get; set; }

        /// <summary>
        /// Base imponible total de la nota de entrega
        /// </summary>
        public decimal BaseImponible { get; set; }
    }
}
