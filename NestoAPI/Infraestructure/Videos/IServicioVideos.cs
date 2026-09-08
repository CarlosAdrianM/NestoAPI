using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Videos
{
    public interface IServicioVideos
    {
        Task<List<VideoLookupModel>> BuscarVideos(string query, bool tieneComprasRecientes, bool soloProtocolos = false, int skip = 0, int take = 20);
        /// <param name="incluirBajas">
        /// Si es true devuelve también los vídeos retirados, marcados con su FechaBaja, para que la
        /// tienda online no tenga que deducir la baja de que un vídeo haya dejado de aparecer.
        /// </param>
        Task<List<VideoLookupModel>> GetVideos(int skip, int take, bool tieneComprasRecientes, bool soloProtocolos = false, bool incluirBajas = false);
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
