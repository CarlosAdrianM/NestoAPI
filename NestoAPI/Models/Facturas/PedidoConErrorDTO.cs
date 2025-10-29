using System;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Información de un pedido que tuvo errores durante el proceso de facturación
    /// </summary>
    public class PedidoConErrorDTO
    {
        /// <summary>
        /// Código de empresa
        /// </summary>
        public string Empresa { get; set; }

        /// <summary>
        /// Número del pedido
        /// </summary>
        public int NumeroPedido { get; set; }

        /// <summary>
        /// Código del cliente
        /// </summary>
        public string Cliente { get; set; }

        /// <summary>
        /// Contacto del cliente
        /// </summary>
        public string Contacto { get; set; }

        /// <summary>
        /// Nombre del cliente
        /// </summary>
        public string NombreCliente { get; set; }

        /// <summary>
        /// Ruta del pedido
        /// </summary>
        public string Ruta { get; set; }

        /// <summary>
        /// Periodo de facturación (NRM, FDM, etc.)
        /// </summary>
        public string PeriodoFacturacion { get; set; }

        /// <summary>
        /// Tipo de error: "Albarán", "Factura", "Impresión", etc.
        /// </summary>
        public string TipoError { get; set; }

        /// <summary>
        /// Mensaje de error detallado
        /// </summary>
        public string MensajeError { get; set; }

        /// <summary>
        /// Fecha de entrega del pedido
        /// </summary>
        public DateTime FechaEntrega { get; set; }

        /// <summary>
        /// Total del pedido
        /// </summary>
        public decimal Total { get; set; }
    }
}
