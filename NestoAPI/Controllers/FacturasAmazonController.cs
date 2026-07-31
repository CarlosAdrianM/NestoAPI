using NestoAPI.Infraestructure.CanalesExternos.Amazon;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.CanalesExternos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#366: facturar pedidos de Amazon y subir la factura (PDF) a Amazon con el feed
    /// UPLOAD_VAT_INVOICE. Lo consume la ventana CanalesExternos → Pedidos → Amazon de Nesto
    /// (Nesto#434), pero el núcleo vive aquí para cualquier cliente.
    /// </summary>
    [Authorize]
    public class FacturasAmazonController : ApiController
    {
        private readonly NVEntities db;
        private readonly IServicioFacturasAmazon servicio;

        public FacturasAmazonController()
        {
            db = new NVEntities();
            servicio = new ServicioFacturasAmazon(
                db,
                new GestorFacturas(new ServicioFacturas()),
                new AmazonFeedsGateway(new AmazonCredencialStore(db)),
                new AlmacenFacturasAmazon(db),
                new Infraestructure.AlbaranesVenta.ServicioAlbaranesVenta(db));
        }

        public FacturasAmazonController(IServicioFacturasAmazon servicio)
        {
            this.servicio = servicio;
        }

        /// <summary>
        /// Factura el pedido (si no lo está ya) y sube el PDF de la factura a Amazon. Idempotente:
        /// volver a llamar con el mismo pedido reemplaza la factura subida.
        /// </summary>
        [HttpPost]
        [Route("api/CanalesExternos/Amazon/SubirFactura")]
        [ResponseType(typeof(SubirFacturaAmazonResponseDTO))]
        public async Task<IHttpActionResult> SubirFactura([FromBody] SubirFacturaAmazonRequestDTO peticion)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Empresa) || peticion.Pedido <= 0)
            {
                return BadRequest("Hay que indicar Empresa y Pedido.");
            }
            try
            {
                SubirFacturaAmazonResponseDTO respuesta = await servicio
                    .FacturarYSubirAsync(peticion.Empresa, peticion.Pedido, User?.Identity?.Name)
                    .ConfigureAwait(false);
                return Ok(respuesta);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Estado de subida de varios pedidos (lista separada por comas), para pintar el grid.
        /// </summary>
        [HttpGet]
        [Route("api/CanalesExternos/Amazon/FacturasSubidas")]
        [ResponseType(typeof(List<FacturaSubidaAmazonDTO>))]
        public IHttpActionResult GetFacturasSubidas(string empresa, string pedidos)
        {
            if (string.IsNullOrWhiteSpace(empresa) || string.IsNullOrWhiteSpace(pedidos))
            {
                return BadRequest("Hay que indicar empresa y pedidos.");
            }
            List<int> numeros = new List<int>();
            foreach (string trozo in pedidos.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(trozo.Trim(), out int numero))
                {
                    return BadRequest($"'{trozo.Trim()}' no es un número de pedido válido.");
                }
                numeros.Add(numero);
            }
            return Ok(servicio.ConsultarSubidas(empresa, numeros).ToList());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
