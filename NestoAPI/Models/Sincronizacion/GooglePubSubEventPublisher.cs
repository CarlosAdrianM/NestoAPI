using Google.Cloud.PubSub.V1;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Protobuf;
using System.Configuration;

namespace NestoAPI.Models.Sincronizacion
{
    public class GooglePubSubEventPublisher : ISincronizacionEventPublisher
    {
        private readonly string _projectId;
        private readonly Lazy<PublisherServiceApiClient> _publisherClient;

        public GooglePubSubEventPublisher()
        {
            _projectId = ConfigurationManager.AppSettings["GoogleCloudPubSubProjectId"];
            _publisherClient = new Lazy<PublisherServiceApiClient>(PublisherServiceApiClient.Create);
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

                await _publisherClient.Value.PublishAsync(topicName, new[] { pubsubMessage });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error publicando en Pub/Sub: {ex.Message}");
                throw; // Relanza la excepción para que pueda ser manejada en niveles superiores
            }
        }
    }
}
