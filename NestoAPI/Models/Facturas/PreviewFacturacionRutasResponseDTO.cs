using System.Collections.Generic;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Response con el preview (simulación) de facturación de rutas.
    /// NO crea nada en la BD, solo muestra QUÉ se facturaría.
    /// </summary>
    public class PreviewFacturacionRutasResponseDTO
    {
        public PreviewFacturacionRutasResponseDTO()
        {
            PedidosMuestra = new List<PedidoPreviewDTO>();
        }

        /// <summary>
        /// Número total de pedidos que se procesarían
        /// </summary>
        public int NumeroPedidos { get; set; }

        /// <summary>
        /// Número estimado de albaranes que se crearían
        /// (puede no ser exacto si hay errores en el proceso real)
        /// </summary>
        public int NumeroAlbaranes { get; set; }

        /// <summary>
        /// Número estimado de facturas que se crearían
        /// (depende de MantenerJunto y periodo de facturación)
        /// </summary>
        public int NumeroFacturas { get; set; }

        /// <summary>
        /// Número estimado de notas de entrega que se crearían
        /// (pedidos con NotaEntrega = true)
        /// </summary>
        public int NumeroNotasEntrega { get; set; }

        /// <summary>
        /// Base imponible total de los albaranes
        /// </summary>
        public decimal BaseImponibleAlbaranes { get; set; }

        /// <summary>
        /// Base imponible total de las facturas
        /// </summary>
        public decimal BaseImponibleFacturas { get; set; }

        /// <summary>
        /// Base imponible total de las notas de entrega
        /// </summary>
        public decimal BaseImponibleNotasEntrega { get; set; }

        /// <summary>
        /// Muestra de los primeros pedidos (máximo 20) para verificación
        /// </summary>
        public List<PedidoPreviewDTO> PedidosMuestra { get; set; }
    }

    /// <summary>
    /// Información resumida de un pedido para el preview
    /// </summary>
    public class PedidoPreviewDTO
    {
        /// <summary>
        /// Número de pedido
        /// </summary>
        public int NumeroPedido { get; set; }

        /// <summary>
        /// Cliente
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
        /// Periodo de facturación (NRM, FDM)
        /// </summary>
        public string PeriodoFacturacion { get; set; }

        /// <summary>
        /// Base imponible del pedido (suma de líneas)
        /// </summary>
        public decimal BaseImponible { get; set; }

        /// <summary>
        /// Indica si se creará un albarán para este pedido
        /// </summary>
        public bool CreaAlbaran { get; set; }

        /// <summary>
        /// Indica si se creará una factura para este pedido
        /// (depende de NRM y MantenerJunto)
        /// </summary>
        public bool CreaFactura { get; set; }

        /// <summary>
        /// Indica si se creará una nota de entrega para este pedido
        /// (campo NotaEntrega = true)
        /// </summary>
        public bool CreaNotaEntrega { get; set; }

        /// <summary>
        /// Comentarios del pedido (para verificar "factura física")
        /// </summary>
        public string Comentarios { get; set; }
    }
}
