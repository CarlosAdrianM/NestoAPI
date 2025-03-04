using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.AlbaranesVenta
{
    public interface IGestorAlbaranesVenta
    {
        Task<int> CrearAlbaran(string empresa, int pedido);
    }
}
