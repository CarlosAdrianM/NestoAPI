using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models;
using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// «Dónde está lo que falta» (19/11/26, pedido 930942): el vendedor veía el pedido sin picking y la línea pendiente, y no sabía que la unidad
    /// venía en la reposición 81463 en la furgoneta de Miguel. Va aparte de PedidosVentaController (adelgazarlo). Lógica en
    /// <see cref="ServicioDondeEstaLoQueFalta"/>.
    /// </summary>
    [Authorize]
    public class DondeEstaLoQueFaltaController : ApiController
    {
        public const string MENSAJE_CLIENTE_TIENDA = "Esta consulta es solo para empleados y vendedores.";

        private readonly NVEntities db;
        private IServicioDondeEstaLoQueFalta servicio;

        public DondeEstaLoQueFaltaController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
        }

        /// <summary>Para los tests.</summary>
        internal DondeEstaLoQueFaltaController(IServicioDondeEstaLoQueFalta servicio)
        {
            this.servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));
        }

        internal IServicioDondeEstaLoQueFalta Servicio => servicio ?? (servicio = new ServicioDondeEstaLoQueFalta(
            new ServicioFechaEntregaAgencia(db),
            new LectorLineasDondeEstaLoQueFaltaSql(db),
            (empresa, traspasos) => new ServicioSeguimientoReposiciones(new RepositorioSeguimientoReposicionesSql(db)).SeguimientosTanda(empresa, traspasos)));

        // GET api/PedidosVenta/1/930942/DondeEstaLoQueFalta
        /// <summary>
        /// Por cada producto del pedido que NO está listo en Algete, dónde está (de camino en una reposición, en una tienda, del proveedor, sin
        /// fecha, pack que se monta, kit) con una frase hecha. Todo en Algete: lista vacía y Mensaje. Mismos permisos de lectura que el pedido
        /// (GET api/PedidosVenta: empleados y vendedores); el cliente de la tienda online, 403. 404 si el pedido no existe.
        /// </summary>
        [HttpGet]
        [Route("api/PedidosVenta/{empresa}/{pedido:int}/DondeEstaLoQueFalta")]
        [ResponseType(typeof(DondeEstaLoQueFaltaDTO))]
        public async Task<IHttpActionResult> GetDondeEstaLoQueFalta(string empresa, int pedido)
        {
            if (EsClienteTienda())
            {
                return Content(HttpStatusCode.Forbidden, new HttpError(MENSAJE_CLIENTE_TIENDA));
            }
            DondeEstaLoQueFaltaDTO resultado = await Servicio.Calcular(empresa, pedido).ConfigureAwait(false);
            return resultado == null ? (IHttpActionResult)NotFound() : Ok(resultado);
        }

        /// <summary>El JWT de la tienda online lleva el claim «cliente» (como en CambiarCliente de PedidosVentaController).</summary>
        private bool EsClienteTienda() => (User?.Identity as ClaimsIdentity)?.FindFirst("cliente") != null;

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
