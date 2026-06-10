using System;
using Newtonsoft.Json;

namespace NestoAPI.Models.CanalesExternos
{
    /// <summary>
    /// NestoAPI#225: estructura de las notificaciones que Amazon SP-API entrega a la cola SQS
    /// para la rotación de credenciales. Solo se modelan los campos que usamos.
    /// (Equivalente al NotificationPayload del sample oficial amzn/selling-partner-api-samples.)
    /// </summary>
    public class AmazonNotificationPayload
    {
        public const string TipoNuevoSecreto = "APPLICATION_OAUTH_CLIENT_NEW_SECRET";
        public const string TipoCaducidadSecreto = "APPLICATION_OAUTH_CLIENT_SECRET_EXPIRY";

        [JsonProperty("notificationVersion")]
        public string NotificationVersion { get; set; }

        [JsonProperty("notificationType")]
        public string NotificationType { get; set; }

        [JsonProperty("payload")]
        public PayloadData Payload { get; set; }

        public class PayloadData
        {
            [JsonProperty("applicationOAuthClientNewSecret")]
            public NewSecret ApplicationOAuthClientNewSecret { get; set; }

            [JsonProperty("applicationOAuthClientSecretExpiry")]
            public SecretExpiry ApplicationOAuthClientSecretExpiry { get; set; }
        }

        public class NewSecret
        {
            [JsonProperty("clientId")]
            public string ClientId { get; set; }

            [JsonProperty("newClientSecret")]
            public string NewClientSecret { get; set; }

            [JsonProperty("newClientSecretExpiryTime")]
            public DateTime? NewClientSecretExpiryTime { get; set; }

            [JsonProperty("oldClientSecretExpiryTime")]
            public DateTime? OldClientSecretExpiryTime { get; set; }
        }

        public class SecretExpiry
        {
            [JsonProperty("clientId")]
            public string ClientId { get; set; }

            [JsonProperty("clientSecretExpiryTime")]
            public DateTime? ClientSecretExpiryTime { get; set; }

            [JsonProperty("clientSecretExpiryReason")]
            public string ClientSecretExpiryReason { get; set; }
        }
    }
}
