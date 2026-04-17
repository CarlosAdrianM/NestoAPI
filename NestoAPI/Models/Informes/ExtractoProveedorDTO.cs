using System;

namespace NestoAPI.Models.Informes
{
    /// <summary>
    /// Apunte individual del extracto contable de un proveedor.
    /// Signo del importe (convención de la tabla <c>ExtractoProveedor</c>):
    /// <list type="bullet">
    /// <item>positivo → HABER (factura del proveedor pendiente de pagar)</item>
    /// <item>negativo → DEBE (pago o compensación realizada)</item>
    /// </list>
    /// </summary>
    public class ExtractoProveedorDTO
    {
        /// <summary>NºOrden del apunte en la tabla Contabilidad.</summary>
        public int Id { get; set; }

        public DateTime Fecha { get; set; }

        /// <summary>NºDocumento interno de Nesto (factura, nota de abono, pago…).</summary>
        public string Documento { get; set; }

        /// <summary>NºDocumentoProv: identificador del documento del proveedor (p. ej. Amazon InvoiceId).</summary>
        public string DocumentoProveedor { get; set; }

        public string Concepto { get; set; }

        public decimal Importe { get; set; }

        /// <summary>Importe pendiente (para facturas con pagos parciales).</summary>
        public decimal ImportePendiente { get; set; }

        public string TipoApunte { get; set; }

        public string FormaPago { get; set; }

        public string Efecto { get; set; }

        public string Delegacion { get; set; }

        public string FormaVenta { get; set; }
    }
}
