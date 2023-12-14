using NestoAPI.Models;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Kits
{
    public interface IProductoService
    {
        Task<ProductoDTO.StockProducto> CalcularStockProducto(string producto, string almacen);
        Task<ProductoDTO> LeerProducto(string empresa, string id, bool fichaCompleta);
    }
}