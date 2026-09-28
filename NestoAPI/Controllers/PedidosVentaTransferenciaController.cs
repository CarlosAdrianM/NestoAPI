using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Sugerencia 396 de Novedades: datos para que el cliente pague por transferencia un pedido
    /// prepago. Aparte de PedidosVentaController para no engordarlo más; la ruta cuelga del pedido.
    /// </summary>
    [Authorize]
    public class PedidosVentaTransferenciaController : ApiController
    {
        private readonly NVEntities db;
        private readonly IServicioDatosTransferenciaPedido servicio;

        public PedidosVentaTransferenciaController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            servicio = new ServicioDatosTransferenciaPedido(db);
        }

        public PedidosVentaTransferenciaController(IServicioDatosTransferenciaPedido servicio)
        {
            this.servicio = servicio;
        }

        /// <summary>
        /// IBAN y beneficiario (los mismos del aviso de facturas vencidas), concepto «Cliente NNNNN -
        /// Pedido MMMMMM», importe del pedido y el texto listo para pegar. 404 si no existe el pedido;
        /// 400 si la empresa no tiene cuenta bancaria.
        /// </summary>
        [HttpGet]
        [Route("api/PedidosVenta/{empresa}/{numero:int}/DatosTransferencia")]
        [ResponseType(typeof(DatosTransferenciaPedidoDTO))]
        public async Task<IHttpActionResult> GetDatosTransferencia(string empresa, int numero)
        {
            // Solo empleados y vendedores: un cliente de la tienda (JWT con claim "cliente") no consulta pedidos por número.
            if ((User?.Identity as ClaimsIdentity)?.FindFirst("cliente") != null)
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            string empresaPedido = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            DatosTransferenciaPedidoDTO datos = await servicio.Leer(empresaPedido, numero).ConfigureAwait(false);
            if (datos == null)
            {
                return NotFound();
            }
            if (string.IsNullOrWhiteSpace(datos.Iban))
            {
                return BadRequest($"La empresa {empresaPedido} no tiene ninguna cuenta bancaria para recibir transferencias.");
            }
            return Ok(datos);
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
