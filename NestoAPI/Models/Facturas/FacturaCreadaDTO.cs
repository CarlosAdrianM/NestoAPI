namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Representa una factura creada durante la facturación de rutas.
    /// Hereda de DocumentoImprimibleDTO las propiedades comunes (Empresa, NumeroPedido, Cliente, etc.) y DatosImpresion.
    /// </summary>
    public class FacturaCreadaDTO : DocumentoImprimibleDTO
    {
        /// <summary>
        /// Número de factura creada
        /// </summary>
        public string NumeroFactura { get; set; }

        /// <summary>
        /// Serie de la factura
        /// </summary>
        public string Serie { get; set; }
    }
}
