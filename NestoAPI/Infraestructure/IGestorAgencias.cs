using NestoAPI.Models;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public interface IGestorAgencias
    {
        Task<RespuestaAgencia> SePuedeServirPedido(PedidoVentaDTO pedido, IServicioAgencias servicio, IGestorStocks gestorStocks);
    }
}
