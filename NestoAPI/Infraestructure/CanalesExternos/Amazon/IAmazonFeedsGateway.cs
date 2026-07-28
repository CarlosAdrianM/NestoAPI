using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CanalesExternos.Amazon
{
    /// <summary>
    /// NestoAPI#366: acceso HTTP a la SP-API de vendedor (Orders + Feeds 2021-06-30) para subir
    /// facturas a Amazon. Las llamadas SP-API de negocio solo requieren el token LWA del vendedor
    /// (x-amz-access-token, grant refresh_token de la credencial de #225); no llevan firma AWS.
    /// Se abstrae en interfaz para poder testear la orquestación con FakeItEasy.
    /// </summary>
    public interface IAmazonFeedsGateway
    {
        /// <summary>Datos del pedido de Amazon (GET /orders/v0/orders/{orderId}); sirve para
        /// resolver el MarketplaceId real, que Nesto no persiste.</summary>
        Task<AmazonPedidoInfo> ObtenerPedidoAsync(string amazonOrderId);

        /// <summary>createFeedDocument: reserva el documento y devuelve la URL de subida.</summary>
        Task<AmazonFeedDocumento> CrearDocumentoFeedAsync(string contentType);

        /// <summary>PUT del contenido a la URL presignada del documento (sin token: la URL ya va firmada).</summary>
        Task SubirDocumentoAsync(string url, byte[] contenido, string contentType);

        /// <summary>createFeed: encola el feed y devuelve su feedId.</summary>
        Task<string> CrearFeedAsync(string feedType, string marketplaceId, string feedDocumentId,
            IReadOnlyDictionary<string, string> feedOptions);

        /// <summary>getFeed: estado de proceso del feed (IN_QUEUE/IN_PROGRESS/DONE/CANCELLED/FATAL).</summary>
        Task<AmazonFeedEstado> ObtenerFeedAsync(string feedId);

        /// <summary>Descarga el informe de resultado del feed (getFeedDocument + GET de la URL,
        /// descomprimiendo GZIP si procede). Null si no hay documento.</summary>
        Task<string> DescargarInformeFeedAsync(string feedDocumentId);
    }

    public class AmazonPedidoInfo
    {
        public string AmazonOrderId { get; set; }
        public string MarketplaceId { get; set; }
        public string SalesChannel { get; set; }
        public string OrderStatus { get; set; }
        public string FulfillmentChannel { get; set; }
    }

    public class AmazonFeedDocumento
    {
        public string FeedDocumentId { get; set; }
        public string Url { get; set; }
    }

    public class AmazonFeedEstado
    {
        public string FeedId { get; set; }
        public string ProcessingStatus { get; set; }
        public string ResultFeedDocumentId { get; set; }
    }
}
