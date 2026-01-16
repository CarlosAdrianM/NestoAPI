using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Videos
{
    public interface IServicioVideos
    {
        Task<List<VideoLookupModel>> BuscarVideos(string query, bool tieneComprasRecientes, int skip = 0, int take = 20);
        Task<List<VideoLookupModel>> GetVideos(int skip, int take, bool tieneComprasRecientes);
        Task<List<VideoLookupModel>> GetVideosConProducto(string productoId);

        /// <summary>
        /// Obtiene un videoprotocolo para mostrar en correos promocionales.
        /// Por ahora devuelve el último videoprotocolo publicado.
        /// En el futuro puede personalizarse según el cliente y sus compras.
        /// </summary>
        /// <param name="cliente">Cliente al que se enviará el correo (para futura personalización)</param>
        /// <returns>VideoLookupModel del videoprotocolo o null si no hay ninguno</returns>
        Task<VideoLookupModel> ObtenerVideoprotocoloParaCorreo(string cliente = null);
    }
}
