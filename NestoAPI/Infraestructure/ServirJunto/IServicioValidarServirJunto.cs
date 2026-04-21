using NestoAPI.Models.PedidosVenta.ServirJunto;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ServirJunto
{
    /// <summary>
    /// Servicio que encapsula el pipeline de validación del "Servir junto" del pedido.
    /// Issue NestoAPI#161: extraído para que el endpoint nuevo
    /// (api/PedidosVenta/ValidarServirJunto) y el endpoint obsoleto en
    /// GanavisionesController compartan la misma lógica sin duplicarla.
    /// </summary>
    public interface IServicioValidarServirJunto
    {
        Task<ValidarServirJuntoResponse> Validar(ValidarServirJuntoRequest request);
    }
}
