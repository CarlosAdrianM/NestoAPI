using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace NestoAPI.Infraestructure.CanalesExternos.Amazon
{
    /// <summary>
    /// NestoAPI#225: implementación HTTP del gateway de rotación de Amazon.
    /// Port del flujo PowerShell validado el 10/06/2026:
    ///   1) token LWA client_credentials con scope sellingpartnerapi::client_credential:rotation
    ///   2) POST {EU}/applications/2023-11-30/clientSecret  -> 204 (el secreto llega por SQS)
    ///   3) SQS ReceiveMessage / DeleteMessage firmados con SigV4 (host;x-amz-date)
    /// Las llamadas SP-API ya no requieren SigV4 (solo token LWA); SQS sí.
    /// </summary>
    public class AmazonSpApiGateway : IAmazonSpApiGateway
    {
        private const string SqsApiVersion = "2012-11-05";
        private static readonly HttpClient ClienteCompartido = new HttpClient();

        private readonly HttpClient _http;
        private readonly AmazonSpApiOpciones _opciones;

        public AmazonSpApiGateway(AmazonSpApiOpciones opciones, HttpClient http = null)
        {
            _opciones = opciones ?? throw new ArgumentNullException(nameof(opciones));
            _http = http ?? ClienteCompartido;
        }

        public async Task<string> ObtenerTokenRotacionAsync(string clientId, string clientSecret)
        {
            FormUrlEncodedContent contenido = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("scope", AmazonSpApiOpciones.ScopeRotacion),
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret)
            });

            using (HttpResponseMessage resp = await _http.PostAsync(_opciones.LwaTokenEndpoint, contenido).ConfigureAwait(false))
            {
                string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    throw new AmazonRotacionException($"Error obteniendo token de rotación LWA ({(int)resp.StatusCode}): {json}");
                }
                string token = (string)JObject.Parse(json)["access_token"];
                if (string.IsNullOrEmpty(token))
                {
                    throw new AmazonRotacionException("La respuesta del token LWA no contiene access_token.");
                }
                return token;
            }
        }

        public async Task RotarClientSecretAsync(string accessTokenRotacion)
        {
            string url = _opciones.EuEndpoint.TrimEnd('/') + "/applications/2023-11-30/clientSecret";
            using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url))
            {
                req.Headers.TryAddWithoutValidation("x-amz-access-token", accessTokenRotacion);
                req.Content = new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json");

                using (HttpResponseMessage resp = await _http.SendAsync(req).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        string cuerpo = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new AmazonRotacionException($"rotateApplicationClientSecret devolvió {(int)resp.StatusCode}: {cuerpo}");
                    }
                    // Éxito esperado: 204 No Content. El secreto nuevo llega a la cola SQS.
                }
            }
        }

        public async Task<IReadOnlyList<AmazonSqsMessage>> RecibirMensajesColaAsync()
        {
            string body = "Action=ReceiveMessage&Version=" + SqsApiVersion +
                          "&MaxNumberOfMessages=10&WaitTimeSeconds=10&VisibilityTimeout=0";
            string xml = await EnviarSqsAsync(body).ConfigureAwait(false);

            XDocument doc = XDocument.Parse(xml);
            List<AmazonSqsMessage> mensajes = new List<AmazonSqsMessage>();
            foreach (XElement m in doc.Descendants().Where(e => e.Name.LocalName == "Message"))
            {
                string cuerpo = m.Elements().FirstOrDefault(e => e.Name.LocalName == "Body")?.Value;
                string receipt = m.Elements().FirstOrDefault(e => e.Name.LocalName == "ReceiptHandle")?.Value;
                if (!string.IsNullOrEmpty(cuerpo))
                {
                    mensajes.Add(new AmazonSqsMessage { Body = cuerpo, ReceiptHandle = receipt });
                }
            }
            return mensajes;
        }

        public async Task BorrarMensajeColaAsync(string receiptHandle)
        {
            string body = "Action=DeleteMessage&Version=" + SqsApiVersion +
                          "&ReceiptHandle=" + Uri.EscapeDataString(receiptHandle);
            _ = await EnviarSqsAsync(body).ConfigureAwait(false);
        }

        private async Task<string> EnviarSqsAsync(string body)
        {
            if (string.IsNullOrEmpty(_opciones.SqsQueueUrl))
            {
                throw new AmazonRotacionException("No está configurada la URL de la cola SQS (AmazonSpApi:SqsQueueUrl).");
            }

            Uri uri = new Uri(_opciones.SqsQueueUrl);
            string host = uri.Host;
            string canonicalUri = uri.AbsolutePath;

            AwsSignatureV4.FirmaResultado firma = AwsSignatureV4.FirmarPost(
                _opciones.AwsAccessKey, _opciones.AwsSecretKey, _opciones.Region, "sqs",
                host, canonicalUri, body, DateTime.UtcNow);

            using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, uri))
            {
                req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
                req.Headers.TryAddWithoutValidation("X-Amz-Date", firma.AmzDate);
                req.Headers.TryAddWithoutValidation("Authorization", firma.Authorization);

                using (HttpResponseMessage resp = await _http.SendAsync(req).ConfigureAwait(false))
                {
                    string contenido = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        throw new AmazonRotacionException($"Error en SQS ({(int)resp.StatusCode}): {contenido}");
                    }
                    return contenido;
                }
            }
        }
    }

    /// <summary>Excepción específica del proceso de rotación, para distinguirla en ELMAH/tests.</summary>
    public class AmazonRotacionException : Exception
    {
        public AmazonRotacionException(string mensaje) : base(mensaje) { }
        public AmazonRotacionException(string mensaje, Exception inner) : base(mensaje, inner) { }
    }
}
