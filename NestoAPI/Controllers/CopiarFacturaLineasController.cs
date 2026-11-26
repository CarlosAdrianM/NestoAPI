using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.Rectificativas;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using NestoAPI.Models.Rectificativas;
using System;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#899/#863: para copiar o abonar SOLO unos albaranes o unas líneas de una factura (POST api/PedidosVenta/CopiarFactura con
    /// Albaranes/Lineas), Nesto necesita ver las líneas con lo que ya se ha abonado de cada una. Aparte de PedidosVentaController (no
    /// engordarlo). Solo lectura; los mismos usuarios que copian facturas (cualquier empleado autenticado); un cliente de la tienda, 403.
    /// </summary>
    [Authorize]
    public class CopiarFacturaLineasController : ApiController
    {
        private readonly NVEntities db;
        private IGestorCopiaPedidos gestor;

        public CopiarFacturaLineasController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
        }

        internal CopiarFacturaLineasController(IGestorCopiaPedidos gestor)
        {
            this.gestor = gestor ?? throw new ArgumentNullException(nameof(gestor));
        }

        internal IGestorCopiaPedidos Gestor => gestor ?? (gestor = new GestorCopiaPedidos(
            db, new ServicioPedidosVenta(), new ServicioAlbaranesVenta(db), new ServicioFacturas(db)));

        // GET api/PedidosVenta/CopiarFactura/Lineas?empresa=1&factura=NV2616046
        /// <summary>
        /// Las líneas de la factura (o del pedido de una sola factura) con su albarán, cantidad, lo ya abonado y lo que queda por abonar.
        /// 400 sin empresa o factura; 404 si no hay factura.
        /// </summary>
        [HttpGet]
        [Route("api/PedidosVenta/CopiarFactura/Lineas")]
        [ResponseType(typeof(LineasFacturaParaCopiarDTO))]
        public async Task<IHttpActionResult> GetLineas(string empresa = null, string factura = null)
        {
            if (ValidadorAccesoCliente.EsClienteTienda(User))
            {
                return Content(HttpStatusCode.Forbidden, new HttpError(ValidadorAccesoCliente.MENSAJE_SOLO_INTERNO));
            }
            if (string.IsNullOrWhiteSpace(empresa) || string.IsNullOrWhiteSpace(factura))
            {
                return BadRequest("Indique la empresa y la factura.");
            }
            LineasFacturaParaCopiarDTO lineas = await Gestor.LineasParaCopiar(empresa, factura).ConfigureAwait(false);
            if (lineas == null)
            {
                return Content(HttpStatusCode.NotFound, new HttpError($"No se ha encontrado la factura {factura.Trim()}."));
            }
            return Ok(lineas);
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
