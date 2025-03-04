using NestoAPI.Models.Sincronizacion;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    public class SincronizacionEventWrapper
    {
        private readonly ISincronizacionEventPublisher _publisher;

        public SincronizacionEventWrapper(ISincronizacionEventPublisher publisher)
        {
            _publisher = publisher;
        }

        public async Task PublishSincronizacionEventAsync(string topic, object message)
        {
            await _publisher.PublishEventAsync(topic, message);
        }
    }
}
