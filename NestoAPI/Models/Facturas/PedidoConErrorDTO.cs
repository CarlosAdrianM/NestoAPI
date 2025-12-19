using System;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Nivel de severidad de un mensaje en el proceso de facturación
    /// </summary>
    public enum NivelSeveridad
    {
        /// <summary>
        /// Error crítico que impide el proceso (ej: excepción al crear albarán/factura)
        /// </summary>
        Error = 0,

        /// <summary>
        /// Aviso informativo que el usuario puede ignorar (ej: factura pendiente por MantenerJunto)
        /// </summary>
        Warning = 1,

        /// <summary>
        /// Información adicional para el usuario (sin acción requerida)
        /// </summary>
        Info = 2
    }

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
        /// Nivel de severidad del mensaje.
        /// Error: fallo crítico que impide el proceso.
        /// Warning: aviso informativo que el usuario puede ignorar.
        /// Info: información adicional sin acción requerida.
        /// </summary>
        public NivelSeveridad Severidad { get; set; } = NivelSeveridad.Error;

        /// <summary>
        /// Fecha de entrega del pedido
        /// </summary>
        public DateTime FechaEntrega { get; set; }

        /// <summary>
        /// Total del pedido
        /// </summary>
        public decimal Total { get; set; }

        /// <summary>
        /// Información adicional de validación del descuento PP.
        /// Solo se rellena cuando hay un error de descuadre para ayudar al diagnóstico.
        /// </summary>
        public string InfoDescuentoPP { get; set; }
    }
}
