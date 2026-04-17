namespace NestoAPI.Models.PedidosCompra
{
    /// <summary>
    /// Resumen de una factura de compra ya contabilizada, pensado para que los clientes
    /// puedan detectar duplicados en procesos de importación desde canales externos.
    /// </summary>
    public class FacturaContabilizadaProveedorDTO
    {
        /// <summary>Identificador original del documento del proveedor (p. ej. Amazon InvoiceId).</summary>
        public string NumeroDocumentoProv { get; set; }

        /// <summary>Número interno de factura asignado en Nesto al contabilizarla.</summary>
        public int NumeroFactura { get; set; }
    }
}
