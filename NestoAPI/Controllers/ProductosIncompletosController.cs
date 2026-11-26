using NestoAPI.Infraestructure.ProductosIncompletos;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#902: productos incompletos. Sacar una pieza de una unidad (sin que sea un inventario), completarla cuando llega el
    /// recambio o darla de baja, y el listado de las unidades incompletas. Las reglas, en <see cref="ReglasProductosIncompletos"/>; los
    /// errores (400, 403, 404, 409, 503) salen como NestoBusinessException por el filtro global.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Almacen/ProductosIncompletos")]
    public class ProductosIncompletosController : ApiController
    {
        private readonly NVEntities db;
        private readonly IServicioProductosIncompletos servicio;

        public ProductosIncompletosController() : this(new NVEntities())
        {
        }

        internal ProductosIncompletosController(NVEntities db, IServicioProductosIncompletos servicio = null)
        {
            this.db = db;
            if (db != null)
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
            }
            this.servicio = servicio ?? new ServicioProductosIncompletos(db);
        }

        // GET api/Almacen/ProductosIncompletos?soloVivos=true&almacen=ALG&producto=40252
        /// <summary>
        /// Las unidades incompletas (las vivas por defecto): qué les falta, desde cuándo, valor a PVP, nº de serie y dónde están. Almacén,
        /// Compras, Dirección e Informática ven todo; las tiendas, lo suyo.
        /// </summary>
        [HttpGet]
        [Route("")]
        [ResponseType(typeof(ListadoProductosIncompletosDTO))]
        public async Task<IHttpActionResult> GetProductosIncompletos(bool soloVivos = true, string almacen = null, string producto = null)
        {
            return Ok(await servicio.Listar(soloVivos, almacen, producto, User).ConfigureAwait(false));
        }

        // GET api/Almacen/ProductosIncompletos/12
        [HttpGet]
        [Route("{id:int}")]
        [ResponseType(typeof(ProductoIncompletoDTO))]
        public async Task<IHttpActionResult> GetProductoIncompleto(int id)
        {
            return Ok(await servicio.Leer(id, User).ConfigureAwait(false));
        }

        // GET api/Almacen/ProductosIncompletos/PiezasSacadas?producto=40252
        /// <summary>Las piezas que ya se han sacado alguna vez de ese producto (para proponerlas). No es una composición.</summary>
        [HttpGet]
        [Route("PiezasSacadas")]
        [ResponseType(typeof(List<PiezaSacadaDTO>))]
        public async Task<IHttpActionResult> GetPiezasSacadas(string producto)
        {
            return Ok(await servicio.PiezasSacadas(producto, User).ConfigureAwait(false));
        }

        // POST api/Almacen/ProductosIncompletos/SacarPieza?ensayo=true
        /// <summary>
        /// Saca una pieza de una unidad: +N de la pieza en el almacén (Algete: pendiente de ubicar) y la unidad pasa al almacén de
        /// incompletos; etiqueta «INCOMPLETO» por la impresora del usuario y campana a Compras. Con ensayo=true no escribe nada.
        /// </summary>
        [HttpPost]
        [Route("SacarPieza")]
        [ResponseType(typeof(ResultadoProductoIncompletoDTO))]
        public async Task<IHttpActionResult> PostSacarPieza([FromBody] SacarPiezaDTO peticion, bool ensayo = false)
        {
            return Ok(await servicio.SacarPieza(peticion, ensayo, User).ConfigureAwait(false));
        }

        // POST api/Almacen/ProductosIncompletos/12/Completar?ensayo=true
        [HttpPost]
        [Route("{id:int}/Completar")]
        [ResponseType(typeof(ResultadoProductoIncompletoDTO))]
        public async Task<IHttpActionResult> PostCompletar(int id, [FromBody] CompletarProductoIncompletoDTO peticion, bool ensayo = false)
        {
            return Ok(await servicio.Completar(id, peticion, ensayo, User).ConfigureAwait(false));
        }

        // POST api/Almacen/ProductosIncompletos/12/Baja?ensayo=true   { Motivo }
        [HttpPost]
        [Route("{id:int}/Baja")]
        [ResponseType(typeof(ResultadoProductoIncompletoDTO))]
        public async Task<IHttpActionResult> PostBaja(int id, [FromBody] BajaProductoIncompletoDTO peticion, bool ensayo = false)
        {
            return Ok(await servicio.DarDeBaja(id, peticion, ensayo, User).ConfigureAwait(false));
        }

        // POST api/Almacen/ProductosIncompletos/12/Etiqueta
        /// <summary>Vuelve a imprimir la etiqueta «INCOMPLETO» de una ficha viva.</summary>
        [HttpPost]
        [Route("{id:int}/Etiqueta")]
        [ResponseType(typeof(EtiquetaIncompletoDTO))]
        public async Task<IHttpActionResult> PostEtiqueta(int id)
        {
            return Ok(await servicio.ImprimirEtiqueta(id, User).ConfigureAwait(false));
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
