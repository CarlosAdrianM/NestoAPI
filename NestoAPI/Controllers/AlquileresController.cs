using NestoAPI.Infraestructure.Alquileres;
using NestoAPI.Models.Alquileres;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class AlquileresController : ApiController
    {
        private readonly IProductosAlquilerService _productosAlquiler;

        public AlquileresController()
        {
            _productosAlquiler = new ProductosAlquilerService();
        }

        public AlquileresController(IProductosAlquilerService productosAlquiler)
        {
            _productosAlquiler = productosAlquiler;
        }

        [HttpGet]
        [Route("api/Alquileres/Productos")]
        [ResponseType(typeof(List<ProductoAlquilerDTO>))]
        public async Task<IHttpActionResult> GetProductosAlquiler()
        {
            List<ProductoAlquilerDTO> productos = await _productosAlquiler
                .LeerProductosAlquilerAsync()
                .ConfigureAwait(false);

            return Ok(productos);
        }

        // GET: api/Alquileres/Movimientos?empresa=1&pedido=12345
        // Nesto#340 Fase 1C.2: líneas del pedido de venta de un alquiler (pestaña Movimientos).
        [HttpGet]
        [Route("api/Alquileres/Movimientos")]
        [ResponseType(typeof(List<MovimientoAlquilerDTO>))]
        public async Task<IHttpActionResult> GetMovimientosAlquiler(string empresa, int pedido)
        {
            List<MovimientoAlquilerDTO> movimientos = await _productosAlquiler
                .LeerMovimientosAlquilerAsync(empresa, pedido)
                .ConfigureAwait(false);

            return Ok(movimientos);
        }

        // GET: api/Alquileres/Compras?producto=26780&numSerie=ABC123
        // Nesto#340 Fase 1C.2: líneas del pedido de compra del aparato (pestaña Compra).
        [HttpGet]
        [Route("api/Alquileres/Compras")]
        [ResponseType(typeof(List<CompraAlquilerDTO>))]
        public async Task<IHttpActionResult> GetComprasAlquiler(string producto, string numSerie)
        {
            List<CompraAlquilerDTO> compras = await _productosAlquiler
                .LeerComprasAlquilerAsync(producto, numSerie)
                .ConfigureAwait(false);

            return Ok(compras);
        }
    }
}
