using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public interface IGestorAgencias
    {
        Task<RespuestaAgencia> SePuedeServirPedido(PedidoVentaDTO pedido, IServicioAgencias servicio, IGestorStocks gestorStocks);
    }
}
