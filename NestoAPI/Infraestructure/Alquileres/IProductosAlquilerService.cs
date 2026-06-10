using NestoAPI.Models.Alquileres;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Alquileres
{
    public interface IProductosAlquilerService
    {
        Task<List<ProductoAlquilerDTO>> LeerProductosAlquilerAsync();

        Task<List<MovimientoAlquilerDTO>> LeerMovimientosAlquilerAsync(string empresa, int pedido);

        Task<List<CompraAlquilerDTO>> LeerComprasAlquilerAsync(string producto, string numSerie);

        Task<List<ExtractoInmovilizadoDTO>> LeerInmovilizadosAlquilerAsync(string empresa, string numero);

        // Nesto#340 Fase 1C.3: grid principal de Alquileres (cabeceras editables del producto).
        Task<List<AlquilerCabeceraDTO>> LeerCabecerasAlquilerAsync(string empresa, string producto);

        Task<List<AlquilerCabeceraDTO>> GuardarCabecerasAlquilerAsync(string empresa, string producto, List<AlquilerCabeceraDTO> cabeceras, string usuario);
    }
}
