using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.AlbaranesVenta
{
    public interface IServicioAlbaranesVenta
    {
        Task<int> CrearAlbaran(string empresa, int pedido, string usuario);
    }
}