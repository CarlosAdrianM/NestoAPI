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

        /// <summary>
        /// NestoAPI#327: avisos de la facturación (p. ej. NIF no registrado en la AEAT) que tienen que
        /// llegarle al que factura. Antes solo los veía quien facturaba desde el pedido o desde Agencias;
        /// ahora viajan también en la facturación de rutas (mismo camino para todos).
        /// </summary>
        public System.Collections.Generic.List<string> Avisos { get; set; } = new System.Collections.Generic.List<string>();
    }
}
