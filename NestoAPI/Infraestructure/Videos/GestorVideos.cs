using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Videos
{
    public class GestorVideos
    {
        private IServicioVideos _servicio;

        public GestorVideos(IServicioVideos servicio) {
            _servicio = servicio;
        }
        internal static async Task<List<VideoLookupModel>> GetVideos(int skip, int take)
        {
            throw new NotImplementedException();
        }
    }
}
