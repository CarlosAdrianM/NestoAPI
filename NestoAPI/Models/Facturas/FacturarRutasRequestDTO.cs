using System;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Request para facturar pedidos de rutas (propia o agencias)
    /// </summary>
    public class FacturarRutasRequestDTO
    {
        /// <summary>
        /// Tipo de ruta a facturar: Propia (16, AT) o Agencias (FW, 00)
        /// </summary>
        public TipoRutaFacturacion TipoRuta { get; set; }

        /// <summary>
        /// Fecha desde la cual considerar pedidos (fecha de entrega >= esta fecha).
        /// Si es null, se usa DateTime.Today
        /// </summary>
        public DateTime? FechaEntregaDesde { get; set; }
    }

    /// <summary>
    /// Tipo de ruta para facturación
    /// </summary>
    public enum TipoRutaFacturacion
    {
        /// <summary>
        /// Ruta propia: "16" y "AT"
        /// </summary>
        RutaPropia,

        /// <summary>
        /// Rutas de agencias: "FW" y "00"
        /// </summary>
        RutasAgencias
    }
}
