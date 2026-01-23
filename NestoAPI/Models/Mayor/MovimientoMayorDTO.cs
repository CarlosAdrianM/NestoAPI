using System;

namespace NestoAPI.Models.Mayor
{
    /// <summary>
    /// DTO para representar un movimiento en el Mayor de una cuenta.
    /// Usado tanto para clientes como para proveedores.
    /// </summary>
    public class MovimientoMayorDTO
    {
        public DateTime Fecha { get; set; }
        public string Concepto { get; set; }
        public string NumeroDocumento { get; set; }
        public decimal Debe { get; set; }
        public decimal Haber { get; set; }

        /// <summary>
        /// Saldo acumulado calculado (Debe - Haber).
        /// Se calcula durante el proceso de generación del Mayor.
        /// </summary>
        public decimal Saldo { get; set; }

        /// <summary>
        /// Tipo de apunte original (1=Factura, 2=Cartera, 3=Pago, 4=Impagado).
        /// </summary>
        public string TipoApunte { get; set; }
    }
}
