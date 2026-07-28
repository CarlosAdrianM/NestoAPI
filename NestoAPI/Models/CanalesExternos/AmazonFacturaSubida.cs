using System;

namespace NestoAPI.Models.CanalesExternos
{
    /// <summary>
    /// NestoAPI#366: registro de las facturas de venta subidas a Amazon (feed UPLOAD_VAT_INVOICE),
    /// en la tabla dbo.AmazonFacturasSubidas. NO se mapea en el EDMX: se accede por SQL crudo
    /// desde AlmacenFacturasAmazon (único call site). Sirve para la idempotencia (saber qué
    /// pedidos tienen ya factura subida) y para auditar el resultado del feed.
    /// </summary>
    public class AmazonFacturaSubida
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string NumeroFactura { get; set; }
        public string AmazonOrderId { get; set; }
        public string MarketplaceId { get; set; }
        public string FeedId { get; set; }
        public string Estado { get; set; }
        public string Resultado { get; set; }
        public DateTime FechaEnvio { get; set; }
        public DateTime? FechaResultado { get; set; }
        public string Usuario { get; set; }
    }

    /// <summary>Estados del registro: ENVIADA (feed aceptado, pendiente de procesar) y el
    /// processingStatus final de Amazon (DONE, FATAL, CANCELLED). OMITIDA no se persiste: se
    /// calcula al consultar, para los pedidos de clientes de factura simplificada (no se suben).</summary>
    public static class EstadosFacturaAmazon
    {
        public const string ENVIADA = "ENVIADA";
        public const string DONE = "DONE";
        public const string FATAL = "FATAL";
        public const string CANCELLED = "CANCELLED";
        public const string OMITIDA = "OMITIDA";
    }
}
