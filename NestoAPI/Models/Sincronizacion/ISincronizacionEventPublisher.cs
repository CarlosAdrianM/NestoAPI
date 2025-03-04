using System.Threading.Tasks;

namespace NestoAPI.Models.Sincronizacion
{
    public interface ISincronizacionEventPublisher
    {
        Task PublishEventAsync(string topic, object message);
    }
}
