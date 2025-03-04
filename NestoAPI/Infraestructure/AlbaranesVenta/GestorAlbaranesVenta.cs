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
        public async Task<int> CrearAlbaran(string empresa, int pedido)
        {
            try
            {
                int albaran = await _servicio.CrearAlbaran(empresa, pedido);
                return albaran;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al crear el albarán", ex);
            }
        }
    }
}
