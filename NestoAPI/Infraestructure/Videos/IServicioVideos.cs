using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Videos
{
    public interface IServicioVideos
    {
        Task<List<VideoLookupModel>> GetVideos(int skip, int take, bool tieneComprasRecientes);
        Task<List<VideoLookupModel>> BuscarVideos(string query, bool tieneComprasRecientes, int skip = 0, int take = 20);
    }
}
