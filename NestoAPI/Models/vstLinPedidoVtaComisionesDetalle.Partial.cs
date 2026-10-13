namespace NestoAPI.Models
{
    using System;

    /// <summary>
    /// Sugerencia 566 (Marta, NestoApp): en el detalle de comisiones, las líneas ya servidas que todavía no tienen
    /// factura (clientes de fin de mes o de «al completar el pedido») salían con la fecha vacía. Campos solo para la
    /// respuesta (no están en el EDMX, EF no los mapea): la fecha del albarán y el texto listo para pintar.
    /// </summary>
    public partial class vstLinPedidoVtaComisionesDetalle
    {
        /// <summary>Fecha del albarán de la línea. Solo se rellena en el mes en curso (la vista de meses cerrados no la tiene).</summary>
        public DateTime? FechaAlbaran { get; set; }

        /// <summary>
        /// Null si ya está facturada (se pinta la fecha de factura, como siempre); si no, «Servido el 02/10, pendiente de
        /// facturar», «Pendiente de facturar» (sin fecha de albarán conocida) o «Pendiente de servir» (aún sin albarán).
        /// </summary>
        public string EstadoFacturacion => TextoEstadoFacturacion(Fecha_Factura, FechaAlbaran, EstaServida);

        /// <summary>Mes en curso: false si la línea aún no tiene albarán (en picking). Meses cerrados: siempre true.</summary>
        internal bool EstaServida { get; set; } = true;

        internal static string TextoEstadoFacturacion(DateTime? fechaFactura, DateTime? fechaAlbaran, bool servida)
        {
            if (fechaFactura.HasValue)
            {
                return null;
            }
            if (fechaAlbaran.HasValue)
            {
                return $"Servido el {fechaAlbaran.Value:dd/MM}, pendiente de facturar";
            }
            return servida ? "Pendiente de facturar" : "Pendiente de servir";
        }
    }
}
