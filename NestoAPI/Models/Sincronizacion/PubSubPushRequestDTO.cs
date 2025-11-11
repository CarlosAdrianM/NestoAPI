using System;

namespace NestoAPI.Models.Sincronizacion
{
    /// <summary>
    /// Formato de mensaje recibido desde Google Pub/Sub en Push subscriptions
    /// Documentación: https://cloud.google.com/pubsub/docs/push
    /// </summary>
    public class PubSubPushRequestDTO
    {
        /// <summary>
        /// Objeto del mensaje de Pub/Sub
        /// </summary>
        public PubSubMessageDTO Message { get; set; }

        /// <summary>
        /// ID de la suscripción que envió el mensaje
        /// </summary>
        public string Subscription { get; set; }
    }

    /// <summary>
    /// Mensaje de Pub/Sub contenido en el request push
    /// </summary>
    public class PubSubMessageDTO
    {
        /// <summary>
        /// Datos del mensaje en base64
        /// </summary>
        public string Data { get; set; }

        /// <summary>
        /// Atributos del mensaje (opcional)
        /// </summary>
        public object Attributes { get; set; }

        /// <summary>
        /// ID único del mensaje
        /// </summary>
        public string MessageId { get; set; }

        /// <summary>
        /// Timestamp de publicación
        /// </summary>
        public DateTime PublishTime { get; set; }
    }
}
