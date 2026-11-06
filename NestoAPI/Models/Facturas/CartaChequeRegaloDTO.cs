namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// NestoAPI#838: la carta en papel de un cheque regalo que acaba de generar una factura (como el correo, pero para meterla en
    /// el paquete). Va en la respuesta de la facturación con su PDF en DatosImpresion (1 copia) y, con el interruptor
    /// Facturacion:ImprimirDocumentosEnServidor, se imprime en el servidor justo después de su factura (ImpresoEnServidor).
    /// </summary>
    public class CartaChequeRegaloDTO : DocumentoImprimibleDTO
    {
        /// <summary>La factura que generó el cheque.</summary>
        public string NumeroFactura { get; set; }

        /// <summary>«Carta cheque regalo NV26xxxxx» (nombre del documento).</summary>
        public string Nombre { get; set; }

        /// <summary>«la carta del cheque regalo de la factura NV26xxxxx» (para los avisos de la impresión).</summary>
        public string Descripcion { get; set; }
    }
}
