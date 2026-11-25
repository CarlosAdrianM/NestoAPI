using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Campanas.OfertasParaTi;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// «Ofertas para ti» en la app de clientes (TiendasNuevaVision): los 4-5 productos en oferta de una campaña vigente que más
    /// probablemente compre el cliente (<see cref="ServicioOfertasCliente"/>). El cliente sale SIEMPRE del claim «cliente» del JWT (sin
    /// él, 401, como PedidosClienteController). Aparte de PedidosClienteController para no engordarlo.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Pedidos")]
    public class OfertasClienteController : ApiController
    {
        private readonly NVEntities db;
        private readonly IServicioOfertasCliente servicio;

        // AddControllersAsServices elige el constructor público que puede construir entero: este.
        public OfertasClienteController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            servicio = ServicioOfertasCliente.Produccion(db);
        }

        internal OfertasClienteController(IServicioOfertasCliente servicio)
        {
            this.servicio = servicio;
        }

        /// <summary>Hoy (sustituible en las pruebas).</summary>
        internal Func<DateTime> Hoy { get; set; } = () => DateTime.Today;

        // GET api/Pedidos/Cliente/Ofertas?campana=Black%20Friday%202026&maximo=5
        /// <summary>
        /// Siempre 200 (sin campaña vigente o sin ofertas, <c>HayOfertas</c> false y la lista vacía). Sin <paramref name="campana"/>, de
        /// cualquier campaña vigente. <paramref name="maximo"/> de 1 a 10 (fuera de rango, el más cercano). Quien no ve precios (cargo 30)
        /// recibe las ofertas sin importes; quien no ve descuentos (cargo 31), con la tarifa y sin descuento.
        /// </summary>
        [HttpGet]
        [Route("Cliente/Ofertas")]
        [ResponseType(typeof(OfertasParaTiRespuestaDTO))]
        public async Task<IHttpActionResult> GetOfertas(string campana = null, int? maximo = null)
        {
            ClaimsIdentity identity = User?.Identity as ClaimsIdentity;
            string cliente = identity?.FindFirst("cliente")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(cliente) || !ValidadorAccesoCliente.ValidarAcceso(identity, cliente).Autorizado)
            {
                return Unauthorized();
            }
            OfertasParaTiRespuestaDTO respuesta = await servicio.Calcular(cliente, campana, maximo, PoliticaPreciosOcultos.NivelDe(identity), Hoy())
                .ConfigureAwait(false);
            return Ok(respuesta);
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
