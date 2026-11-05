using Google.Cloud.PubSub.V1;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using System.Configuration;
using System.Globalization;
using NestoAPI.Infraestructure;

namespace NestoAPI.Models.Sincronizacion
{
    public class GooglePubSubEventPublisher : ISincronizacionEventPublisher
    {
        /// <summary>
        /// NestoAPI#820 (incidente 04-05/11/26): segundos que se espera a Pub/Sub por mensaje. Se cambia sin desplegar con
        /// <c>&lt;add key="GoogleCloudPubSubTimeoutSegundos" value="60" /&gt;</c> en appSettings; sin la clave (o con un valor
        /// no válido), 60 s. Un mensaje normal tarda milisegundos: 60 s solo vence si la red o Pub/Sub no contestan.
        /// </summary>
        internal const string CLAVE_TIMEOUT = "GoogleCloudPubSubTimeoutSegundos";
        internal const int TIMEOUT_POR_DEFECTO_SEGUNDOS = 60;

        private readonly string _projectId;
        private readonly Func<TopicName, PubsubMessage, CancellationToken, Task> _publicar;
        private readonly TimeSpan _tiempoMaximo;

        public GooglePubSubEventPublisher()
        {
            _projectId = ConfigurationManager.AppSettings["GoogleCloudPubSubProjectId"];
            var cliente = new Lazy<PublisherServiceApiClient>(PublisherServiceApiClient.Create);
            _publicar = (topic, mensaje, token) => cliente.Value.PublishAsync(topic, new[] { mensaje }, token);
            _tiempoMaximo = LeerTiempoMaximo(ConfigurationManager.AppSettings[CLAVE_TIMEOUT]);
        }

        internal static TimeSpan LeerTiempoMaximo(string valor)
        {
            return int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out int segundos) && segundos > 0
                ? TimeSpan.FromSeconds(segundos)
                : TimeSpan.FromSeconds(TIMEOUT_POR_DEFECTO_SEGUNDOS);
        }

        public async Task PublishEventAsync(string topic, object message)
        {
            try
            {
                string stringMessage = JsonSerializer.Serialize(message);
                var topicName = TopicName.FromProjectTopic(_projectId, topic);
                var pubsubMessage = new PubsubMessage
                {
                    Data = ByteString.CopyFromUtf8(stringMessage)
                };

                // NestoAPI#820: antes se esperaba sin límite; con la red caída el job de sincronización se quedó colgado 14 horas.
                await EsperaConTimeout.Ejecutar(
                    token => _publicar(topicName, pubsubMessage, token),
                    _tiempoMaximo,
                    "Pub/Sub",
                    $"al publicar en el topic {topic}").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error publicando en Pub/Sub: {ex.Message}");
                throw; // Relanza la excepción para que pueda ser manejada en niveles superiores
            }
        }
    }
}
