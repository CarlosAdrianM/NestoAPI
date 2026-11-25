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

        // GET api/Pedidos/Cliente/Ofertas/Todas?campana=Black%20Friday%202026&desde=0&cuantas=20&familia=&texto=
        /// <summary>
        /// «Ver todas las ofertas»: el ranking del cliente (lo que más le cuadra primero) desde la posición <paramref name="desde"/>, con
        /// hasta <paramref name="cuantas"/> (1 a 50, por defecto 20) que le MEJORAN el precio. <paramref name="familia"/> (código o nombre) y
        /// <paramref name="texto"/> (nombre o referencia; sin tildes ni mayúsculas) filtran antes de paginar. <c>Siguiente</c> = el
        /// <c>desde</c> de la página siguiente (null si no hay más). Mismas reglas de identidad y de precios ocultos que GetOfertas.
        /// </summary>
        [HttpGet]
        [Route("Cliente/Ofertas/Todas")]
        [ResponseType(typeof(TodasOfertasParaTiRespuestaDTO))]
        public async Task<IHttpActionResult> GetTodas(string campana = null, int desde = 0, int? cuantas = null, string familia = null, string texto = null)
        {
            ClaimsIdentity identity = User?.Identity as ClaimsIdentity;
            string cliente = identity?.FindFirst("cliente")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(cliente) || !ValidadorAccesoCliente.ValidarAcceso(identity, cliente).Autorizado)
            {
                return Unauthorized();
            }
            TodasOfertasParaTiRespuestaDTO respuesta = await servicio.Todas(cliente, campana, desde, cuantas, familia, texto,
                PoliticaPreciosOcultos.NivelDe(identity), Hoy()).ConfigureAwait(false);
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
