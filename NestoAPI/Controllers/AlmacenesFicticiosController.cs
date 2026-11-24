using NestoAPI.Infraestructure.AlmacenesFicticios;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#894: los almacenes ficticios (de los vendedores, PRV, AMZ, EXT…): consulta y traspasos contra el almacén propio. Sustituye
    /// a la pantalla «Altas y bajas de almacén ficticio» de Nesto viejo. Las reglas, en <see cref="ReglasAlmacenesFicticios"/>; los
    /// errores (400, 403, 404, 409) salen como NestoBusinessException por el filtro global.
    /// </summary>
    [Authorize]
    public class AlmacenesFicticiosController : ApiController
    {
        private readonly NVEntities db;
        private readonly IServicioAlmacenesFicticios servicio;

        public AlmacenesFicticiosController() : this(new NVEntities())
        {
        }

        internal AlmacenesFicticiosController(NVEntities db, IServicioAlmacenesFicticios servicio = null)
        {
            this.db = db;
            if (db != null)
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
            }
            this.servicio = servicio ?? new ServicioAlmacenesFicticios(db);
        }

        // GET api/AlmacenesFicticios
        /// <summary>
        /// Los almacenes ficticios que puede ver (cada vendedor el suyo; el jefe de equipo, los de su equipo; Almacén, Tiendas, Dirección e
        /// Informática, todos; la tienda online, AMZ), con si puede meter y sacar y contra qué almacén (ALG o su tienda). Un cliente de
        /// la tienda, 403.
        /// </summary>
        [HttpGet]
        [Route("api/AlmacenesFicticios")]
        [ResponseType(typeof(List<AlmacenFicticioDTO>))]
        public async Task<IHttpActionResult> GetAlmacenesFicticios()
        {
            return Ok(await servicio.Listar(User).ConfigureAwait(false));
        }

        // GET api/AlmacenesFicticios/IF/Existencias
        /// <summary>Lo que tiene (saldo ≠ 0 por producto, empresas 1 y 3; sin costes ni precios). 403 si no lo puede ver; 404 si no es un ficticio activo.</summary>
        [HttpGet]
        [Route("api/AlmacenesFicticios/{almacen}/Existencias")]
        [ResponseType(typeof(ExistenciasAlmacenFicticioDTO))]
        public async Task<IHttpActionResult> GetExistencias(string almacen)
        {
            return Ok(await servicio.Existencias(almacen, User).ConfigureAwait(false));
        }

        // POST api/AlmacenesFicticios/Traspasos?ensayo=true   { AlmacenFicticio, Sentido, Lineas[{Producto, Cantidad, Pasillo?, Fila?, Columna?}], Comentario?, Clave? }
        /// <summary>
        /// Entregar (del almacén propio al ficticio) o Recoger (del ficticio al propio), siempre contra el almacén propio. Con ensayo=true
        /// comprueba, propone los huecos de Algete y no escribe nada. 400 si la petición, los productos o el stock no valen; 403 sin
        /// permiso; 404 si no es un ficticio; 409 si el stock o el hueco cambian mientras se guarda.
        /// </summary>
        [HttpPost]
        [Route("api/AlmacenesFicticios/Traspasos")]
        [ResponseType(typeof(ResultadoTraspasoFicticioDTO))]
        public async Task<IHttpActionResult> PostTraspaso([FromBody] TraspasoAlmacenFicticioDTO peticion, bool ensayo = false)
        {
            return Ok(await servicio.Traspasar(peticion, ensayo, User).ConfigureAwait(false));
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
