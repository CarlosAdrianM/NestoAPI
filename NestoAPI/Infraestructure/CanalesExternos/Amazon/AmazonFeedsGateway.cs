using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NestoAPI.Models.CanalesExternos;

namespace NestoAPI.Infraestructure.CanalesExternos.Amazon
{
    /// <summary>
    /// NestoAPI#366: implementación HTTP de la SP-API de vendedor (Orders + Feeds 2021-06-30).
    /// El token LWA se obtiene con la credencial centralizada de #225 (grant refresh_token) y se
    /// cachea unos minutos menos que su caducidad. Reutiliza el patrón de AmazonSpApiGateway
    /// (HttpClient compartido, opciones de Web.config con defaults).
    /// </summary>
    public class AmazonFeedsGateway : IAmazonFeedsGateway
    {
        private const string VersionFeeds = "2021-06-30";
        private static readonly HttpClient ClienteCompartido = new HttpClient();

        private readonly HttpClient _http;
        private readonly AmazonSpApiOpciones _opciones;
        private readonly IAmazonCredencialStore _credenciales;

        private string _tokenCacheado;
        private DateTime _tokenCaducidad = DateTime.MinValue;

        public AmazonFeedsGateway(IAmazonCredencialStore credenciales, AmazonSpApiOpciones opciones = null, HttpClient http = null)
        {
            _credenciales = credenciales ?? throw new ArgumentNullException(nameof(credenciales));
            _opciones = opciones ?? AmazonSpApiOpciones.DesdeConfiguracion();
            _http = http ?? ClienteCompartido;
        }

        public async Task<AmazonPedidoInfo> ObtenerPedidoAsync(string amazonOrderId)
        {
            JObject json = await LlamarSpApiAsync(HttpMethod.Get, $"/orders/v0/orders/{Uri.EscapeDataString(amazonOrderId)}").ConfigureAwait(false);
            JToken payload = json["payload"] ?? json;
            return new AmazonPedidoInfo
            {
                AmazonOrderId = (string)payload["AmazonOrderId"],
                MarketplaceId = (string)payload["MarketplaceId"],
                SalesChannel = (string)payload["SalesChannel"],
                OrderStatus = (string)payload["OrderStatus"],
                FulfillmentChannel = (string)payload["FulfillmentChannel"]
            };
        }

        public async Task<AmazonFeedDocumento> CrearDocumentoFeedAsync(string contentType)
        {
            JObject json = await LlamarSpApiAsync(HttpMethod.Post, $"/feeds/{VersionFeeds}/documents",
                new JObject { ["contentType"] = contentType }).ConfigureAwait(false);
            return new AmazonFeedDocumento
            {
                FeedDocumentId = (string)json["feedDocumentId"],
                Url = (string)json["url"]
            };
        }

        public async Task SubirDocumentoAsync(string url, byte[] contenido, string contentType)
        {
            using (var peticion = new HttpRequestMessage(HttpMethod.Put, url))
            {
                peticion.Content = new ByteArrayContent(contenido);
                peticion.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                using (HttpResponseMessage resp = await _http.SendAsync(peticion).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        string cuerpo = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new HttpRequestException($"Error subiendo el documento del feed a Amazon ({(int)resp.StatusCode}): {cuerpo}");
                    }
                }
            }
        }

        public async Task<string> CrearFeedAsync(string feedType, string marketplaceId, string feedDocumentId,
            IReadOnlyDictionary<string, string> feedOptions)
        {
            var cuerpo = new JObject
            {
                ["feedType"] = feedType,
                ["marketplaceIds"] = new JArray(marketplaceId),
                ["inputFeedDocumentId"] = feedDocumentId
            };
            if (feedOptions != null && feedOptions.Count > 0)
            {
                cuerpo["feedOptions"] = JObject.FromObject(feedOptions.ToDictionary(o => o.Key, o => o.Value));
            }
            JObject json = await LlamarSpApiAsync(HttpMethod.Post, $"/feeds/{VersionFeeds}/feeds", cuerpo).ConfigureAwait(false);
            return (string)json["feedId"];
        }

        public async Task<AmazonFeedEstado> ObtenerFeedAsync(string feedId)
        {
            JObject json = await LlamarSpApiAsync(HttpMethod.Get, $"/feeds/{VersionFeeds}/feeds/{Uri.EscapeDataString(feedId)}").ConfigureAwait(false);
            return new AmazonFeedEstado
            {
                FeedId = (string)json["feedId"],
                ProcessingStatus = (string)json["processingStatus"],
                ResultFeedDocumentId = (string)json["resultFeedDocumentId"]
            };
        }

        public async Task<string> DescargarInformeFeedAsync(string feedDocumentId)
        {
            if (string.IsNullOrEmpty(feedDocumentId))
            {
                return null;
            }
            JObject json = await LlamarSpApiAsync(HttpMethod.Get, $"/feeds/{VersionFeeds}/documents/{Uri.EscapeDataString(feedDocumentId)}").ConfigureAwait(false);
            string url = (string)json["url"];
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }
            byte[] bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
            bool esGzip = string.Equals((string)json["compressionAlgorithm"], "GZIP", StringComparison.OrdinalIgnoreCase);
            return esGzip ? Descomprimir(bytes) : Encoding.UTF8.GetString(bytes);
        }

        private static string Descomprimir(byte[] gzip)
        {
            using (var origen = new MemoryStream(gzip))
            using (var descompresor = new GZipStream(origen, CompressionMode.Decompress))
            using (var lector = new StreamReader(descompresor, Encoding.UTF8))
            {
                return lector.ReadToEnd();
            }
        }

        private async Task<JObject> LlamarSpApiAsync(HttpMethod metodo, string ruta, JObject cuerpo = null)
        {
            string token = await ObtenerTokenVendedorAsync().ConfigureAwait(false);
            using (var peticion = new HttpRequestMessage(metodo, _opciones.EuEndpoint + ruta))
            {
                peticion.Headers.Add("x-amz-access-token", token);
                if (cuerpo != null)
                {
                    peticion.Content = new StringContent(cuerpo.ToString(Formatting.None), Encoding.UTF8, "application/json");
                }
                using (HttpResponseMessage resp = await _http.SendAsync(peticion).ConfigureAwait(false))
                {
                    string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"Error SP-API {metodo} {ruta} ({(int)resp.StatusCode}): {json}");
                    }
                    return string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
                }
            }
        }

        /// <summary>Token LWA del vendedor (grant refresh_token) con la credencial de #225; se
        /// cachea 55 minutos (Amazon lo emite con 1 hora de vida).</summary>
        private async Task<string> ObtenerTokenVendedorAsync()
        {
            if (_tokenCacheado != null && DateTime.UtcNow < _tokenCaducidad)
            {
                return _tokenCacheado;
            }

            AmazonSpApiCredencial credencial = _credenciales.Obtener()
                ?? throw new InvalidOperationException("No hay credencial Amazon en dbo.AmazonSpApiCredencial (NestoAPI#225).");

            var contenido = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", credencial.RefreshToken?.Trim()),
                new KeyValuePair<string, string>("client_id", credencial.ClientId?.Trim()),
                new KeyValuePair<string, string>("client_secret", credencial.ClientSecret?.Trim())
            });
            using (HttpResponseMessage resp = await _http.PostAsync(_opciones.LwaTokenEndpoint, contenido).ConfigureAwait(false))
            {
                string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Error obteniendo token LWA de vendedor ({(int)resp.StatusCode}): {json}");
                }
                JObject objeto = JObject.Parse(json);
                _tokenCacheado = (string)objeto["access_token"];
                _tokenCaducidad = DateTime.UtcNow.AddMinutes(55);
                return _tokenCacheado;
            }
        }
    }
}
