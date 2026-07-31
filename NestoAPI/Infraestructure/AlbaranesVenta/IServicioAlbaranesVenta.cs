using System;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.AlbaranesVenta
{
    public interface IServicioAlbaranesVenta
    {
        // fechaEntrega: el SP solo albaranea líneas con [Fecha Entrega] <= fechaEntrega;
        // sin indicarla se usa la fecha actual (comportamiento histórico de rutas).
        Task<int> CrearAlbaran(string empresa, int pedido, string usuario, DateTime? fechaEntrega = null);
    }
}