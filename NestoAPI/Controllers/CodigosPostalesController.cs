using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.CodigosPostales;
using NestoAPI.Models;
using NestoAPI.Models.CodigosPostales;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// #378: mantenimiento de códigos postales para la ventana de Nesto (Dirección y Tienda
    /// online). Busca por número o población y permite corregir país, ruta, vendedor y los
    /// vendedores por grupo de producto.
    /// </summary>
    [Authorize]
    public class CodigosPostalesController : ApiController
    {
        private readonly NVEntities db;
        private readonly GestorMantenimientoCodigosPostales gestor;

        public CodigosPostalesController()
        {
            db = new NVEntities();
            gestor = new GestorMantenimientoCodigosPostales(db);
        }

        internal CodigosPostalesController(NVEntities db)
        {
            this.db = db;
            gestor = new GestorMantenimientoCodigosPostales(db);
        }

        // GET: api/CodigosPostales?empresa=1&filtro=28004
        [HttpGet]
        [Route("api/CodigosPostales")]
        [ResponseType(typeof(List<CodigoPostalMantenimientoDTO>))]
        public async Task<IHttpActionResult> GetCodigosPostales(string filtro, string empresa = null)
        {
            if (string.IsNullOrWhiteSpace(filtro))
            {
                return BadRequest("Hay que indicar un número de código postal o una población para buscar");
            }
            List<CodigoPostalMantenimientoDTO> lista = await gestor.Buscar(empresa, filtro).ConfigureAwait(false);
            return Ok(lista);
        }

        // PUT: api/CodigosPostales
        [HttpPut]
        [Route("api/CodigosPostales")]
        [ResponseType(typeof(CodigoPostalMantenimientoDTO))]
        public async Task<IHttpActionResult> PutCodigoPostal(CodigoPostalMantenimientoDTO codigoPostal)
        {
            if (codigoPostal == null || string.IsNullOrWhiteSpace(codigoPostal.Numero))
            {
                return BadRequest("Falta el número del código postal");
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, Constantes.Vendedores.VENDEDOR_GENERAL);
            CodigoPostalMantenimientoDTO actualizado = await gestor.Actualizar(codigoPostal, usuario).ConfigureAwait(false);
            if (actualizado == null)
            {
                return NotFound();
            }
            return Ok(actualizado);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
