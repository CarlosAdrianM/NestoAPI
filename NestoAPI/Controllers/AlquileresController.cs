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
    }
}
