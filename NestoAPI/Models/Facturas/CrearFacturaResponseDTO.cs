namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// DTO de respuesta para la creación de facturas
    /// Incluye el número de factura y la empresa donde se facturó
    /// (que puede ser diferente a la empresa original si hubo traspaso)
    /// </summary>
    public class CrearFacturaResponseDTO
    {
        /// <summary>
        /// Número de la factura creada
        /// </summary>
        public string NumeroFactura { get; set; }

        /// <summary>
        /// Empresa donde se facturó el pedido
        /// Puede ser diferente a la empresa original si hubo traspaso a empresa espejo
        /// </summary>
        public string Empresa { get; set; }

        /// <summary>
        /// Número del pedido facturado
        /// </summary>
        public int NumeroPedido { get; set; }
    }
}
