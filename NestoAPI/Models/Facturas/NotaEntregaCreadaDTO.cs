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

        /// <summary>NestoAPI#758: «Entrega» o «Recogida» (sin fila en NotasEntregaCabecera, «Entrega»).</summary>
        public string Tipo { get; set; }

        /// <summary>NestoAPI#758: el Code128 del documento, «NE{pedido}» o «NR{pedido}».</summary>
        public string CodigoDocumento { get; set; }

        /// <summary>NestoAPI#758: bultos de la parada (recogida: los de la nota; entrega: los del envío del packing).</summary>
        public short? Bultos { get; set; }

        /// <summary>NestoAPI#758: el envío de la agencia 14 que es su parada en el reparto propio (null si no es parada).</summary>
        public int? EnvioId { get; set; }

        /// <summary>NestoAPI#758: etiquetas NR{pedido}-{N} impresas al facturar la ruta (solo recogidas de reparto propio).</summary>
        public int EtiquetasRecogidaImpresas { get; set; }
    }
}
