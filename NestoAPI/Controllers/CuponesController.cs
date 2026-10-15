using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#613 (cupones, corte 3 de #610): los cupones de descuento de los eventos. Consultar puede cualquiera de la casa y el
    /// cliente de la tienda online (solo los suyos); emitir uno sin evento, solo Dirección (403 si no). Los errores de negocio
    /// salen como 400 por el filtro global. Aplicarlo en un pedido va por api/PedidosVenta (línea con CodigoCupon).
    /// </summary>
    [Authorize]
    public class CuponesController : ApiController
    {
        private readonly NVEntities db;
        private readonly IServicioCupones servicio;

        public CuponesController() : this(new NVEntities())
        {
        }

        internal CuponesController(NVEntities db, IServicioCupones servicio = null)
        {
            this.db = db;
            db.Configuration.LazyLoadingEnabled = false;
            this.servicio = servicio ?? new ServicioCupones(db);
        }

        // GET api/Clientes/1/15191/Cupones?todos=false
        /// <summary>
        /// Los cupones del cliente: por defecto, los que se pueden usar (Activo) y los que valdrán (Emitido), el que se puede
        /// usar primero; con todos=true, también los usados, anulados y caducados. Para que la plantilla avise «tiene un cupón».
        /// </summary>
        [HttpGet]
        [Route("api/Clientes/{empresa}/{cliente}/Cupones")]
        [ResponseType(typeof(List<CuponDTO>))]
        public async Task<IHttpActionResult> GetCuponesCliente(string empresa, string cliente, bool todos = false)
        {
            return Ok(await servicio.LeerDelCliente(empresa, cliente, todos, User).ConfigureAwait(false));
        }

        // GET api/Cupones/EV-2610-7K3Q
        /// <summary>Un cupón por su código (404 si no existe o, para un cliente de la tienda, si no es suyo).</summary>
        [HttpGet]
        [Route("api/Cupones/{codigo}")]
        [ResponseType(typeof(CuponDTO))]
        public async Task<IHttpActionResult> GetCupon(string codigo)
        {
            return Ok(await servicio.LeerPorCodigo(codigo, User).ConfigureAwait(false));
        }

        // POST api/Cupones   { Empresa, Cliente, Contacto?, Importe, Motivo, ValidoDesde? }
        /// <summary>Cupón sin evento: solo Dirección, con motivo obligatorio. Mismas reglas de uso; caduca a los 6 meses.</summary>
        [HttpPost]
        [Route("api/Cupones")]
        [ResponseType(typeof(CuponDTO))]
        public async Task<IHttpActionResult> PostCupon([FromBody] CrearCuponManualDTO peticion)
        {
            return Ok(await servicio.CrearManual(peticion, User).ConfigureAwait(false));
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
