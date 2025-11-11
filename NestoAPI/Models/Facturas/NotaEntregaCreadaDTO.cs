namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Representa una nota de entrega creada durante el proceso de facturación de rutas.
    /// Las notas de entrega documentan entregas de productos que pueden estar ya facturados o pendientes de facturación.
    /// Hereda de DocumentoCreadoDTO las propiedades comunes (Empresa, NumeroPedido, Cliente, etc.).
    /// NO hereda de DocumentoImprimibleDTO porque las notas de entrega NO se imprimen directamente.
    /// </summary>
    public class NotaEntregaCreadaDTO : DocumentoCreadoDTO
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
