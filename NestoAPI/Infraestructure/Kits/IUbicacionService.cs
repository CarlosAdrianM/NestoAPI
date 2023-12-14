using NestoAPI.Models.Kits;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Kits
{
    public interface IUbicacionService
    {
        Task<int> PersistirMontarKit(List<PreExtractoProductoDTO> preExtractosUbicados);
        Task<List<UbicacionProductoDTO>> LeerUbicacionesProducto(string producto);
    }
}