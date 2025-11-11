using System;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Request para facturar pedidos de rutas.
    /// REFACTORIZACIÓN: TipoRuta ahora es string (Id del tipo) en lugar de enum,
    /// permitiendo agregar nuevos tipos de ruta sin modificar este código.
    /// </summary>
    public class FacturarRutasRequestDTO
    {
        /// <summary>
        /// Id del tipo de ruta a facturar (ej: "PROPIA", "AGENCIA").
        /// El valor debe corresponder a un tipo registrado en TipoRutaFactory.
        /// Puedes obtener los valores válidos desde el endpoint GET /api/FacturacionRutas/TiposRuta
        /// </summary>
        public string TipoRuta { get; set; }

        /// <summary>
        /// Fecha desde la cual considerar pedidos (fecha de entrega >= esta fecha).
        /// Si es null, se usa DateTime.Today
        /// </summary>
        public DateTime? FechaEntregaDesde { get; set; }
    }
}
