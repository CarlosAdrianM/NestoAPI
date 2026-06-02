using NestoAPI.Models.Alquileres;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Alquileres
{
    public interface IProductosAlquilerService
    {
        Task<List<ProductoAlquilerDTO>> LeerProductosAlquilerAsync();

        Task<List<MovimientoAlquilerDTO>> LeerMovimientosAlquilerAsync(string empresa, int pedido);
    }
}
