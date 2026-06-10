using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CanalesExternos.Amazon
{
    /// <summary>
    /// NestoAPI#225: acceso HTTP a Amazon (LWA + SP-API + SQS) para la rotación de credenciales.
    /// Se abstrae en interfaz para poder testear la orquestación con FakeItEasy.
    /// </summary>
    public interface IAmazonSpApiGateway
    {
        /// <summary>Token LWA grantless con scope de rotación (client_credentials).</summary>
        Task<string> ObtenerTokenRotacionAsync(string clientId, string clientSecret);

        /// <summary>POST rotateApplicationClientSecret (endpoint EU). Lanza si no devuelve 2xx.</summary>
        Task RotarClientSecretAsync(string accessTokenRotacion);

        /// <summary>ReceiveMessage de la cola SQS (long poll). Lista vacía si no hay mensajes.</summary>
        Task<IReadOnlyList<AmazonSqsMessage>> RecibirMensajesColaAsync();

        /// <summary>DeleteMessage de la cola SQS.</summary>
        Task BorrarMensajeColaAsync(string receiptHandle);
    }

    public class AmazonSqsMessage
    {
        public string Body { get; set; }
        public string ReceiptHandle { get; set; }
    }

    /// <summary>Opciones de configuración (endpoints, región, cola, credenciales AWS IAM).</summary>
    public class AmazonSpApiOpciones
    {
        public const string ScopeRotacion = "sellingpartnerapi::client_credential:rotation";

        public string LwaTokenEndpoint { get; set; } = "https://api.amazon.com/auth/o2/token";
        public string EuEndpoint { get; set; } = "https://sellingpartnerapi-eu.amazon.com";
        public string Region { get; set; } = "eu-west-1";
        public string SqsQueueUrl { get; set; }
        public string AwsAccessKey { get; set; }
        public string AwsSecretKey { get; set; }

        public static AmazonSpApiOpciones DesdeConfiguracion()
        {
            return new AmazonSpApiOpciones
            {
                LwaTokenEndpoint = ConfigurationManager.AppSettings["AmazonSpApi:LwaTokenEndpoint"]
                    ?? "https://api.amazon.com/auth/o2/token",
                EuEndpoint = ConfigurationManager.AppSettings["AmazonSpApi:EuEndpoint"]
                    ?? "https://sellingpartnerapi-eu.amazon.com",
                Region = ConfigurationManager.AppSettings["AmazonSpApi:Region"] ?? "eu-west-1",
                SqsQueueUrl = ConfigurationManager.AppSettings["AmazonSpApi:SqsQueueUrl"],
                AwsAccessKey = ConfigurationManager.AppSettings["AmazonSpApiAccessKey"],
                AwsSecretKey = ConfigurationManager.AppSettings["AmazonSpApiSecretKey"]
            };
        }
    }
}
