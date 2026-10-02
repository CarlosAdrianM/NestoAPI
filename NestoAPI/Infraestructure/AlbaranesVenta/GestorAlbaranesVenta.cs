using System;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.AlbaranesVenta
{
    public class GestorAlbaranesVenta : IGestorAlbaranesVenta
    {
        private readonly IServicioAlbaranesVenta _servicio;

        public GestorAlbaranesVenta(IServicioAlbaranesVenta servicio)
        {
            _servicio = servicio;
        }
        public async Task<int> CrearAlbaran(string empresa, int pedido, string usuario)
        {
            try
            {
                int albaran = await _servicio.CrearAlbaran(empresa, pedido, usuario);
                return albaran;
            }
            catch (Exception ex)
            {
                // El procedimiento no dice de qué pedido habla: sin esto ELMAH no permite saber a cuál le pasó.
                throw new Exception($"Error al crear el albarán del pedido {empresa?.Trim()}/{pedido}", ex);
            }
        }
    }
}
