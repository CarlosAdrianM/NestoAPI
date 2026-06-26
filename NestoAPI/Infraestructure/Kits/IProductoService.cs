using NestoAPI.Models;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Kits
{
    public interface IProductoService
    {
        // pedidoExcluir (NestoAPI#262): excluye las líneas de ese pedido del cálculo de PendienteEntregar.
        // Necesario para validar "servir junto": al comprobar si una línea del propio pedido se puede
        // servir, su propia reserva NO debe contar contra sí misma (doble conteo). null = no excluye nada.
        Task<ProductoDTO.StockProducto> CalcularStockProducto(string producto, string almacen, int? pedidoExcluir = null);
        Task<ProductoDTO> LeerProducto(string empresa, string id, bool fichaCompleta);
    }
}