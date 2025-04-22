using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Videos
{
    public interface IServicioVideos
    {
        Task<List<VideoLookupModel>> GetVideos(int skip, int take, bool tieneComprasRecientes);
    }
}
