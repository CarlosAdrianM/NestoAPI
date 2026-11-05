using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using NestoAPI.Models.Inventarios;

namespace NestoAPI.Controllers
{
    public partial class InventariosController : ApiController
    {
        private NVEntities db;
        private readonly LectorProductoLeido lector;

        // Carlos 07/12/15: lo pongo para desactivar el Lazy Loading
        public InventariosController() : this(new NVEntities())
        {
        }

        internal InventariosController(NVEntities db) : this(db, new RepositorioCodigosBarras(db))
        {
        }

        /// <param name="codigos">NestoAPI#788: de dónde sale el producto leído (la referencia y los códigos de barras).</param>
        internal InventariosController(NVEntities db, IRepositorioCodigosBarras codigos)
        {
            this.db = db;
            db.Configuration.LazyLoadingEnabled = false;
            lector = new LectorProductoLeido(codigos);
        }

        const int ESTADO_INVENTARIO_SIN_CONTABILIZAR = 1;


        // GET: api/Inventarios
        public IQueryable<Inventario> GetInventarios(string empresa, string almacen, DateTime fecha)
        {
            DateTime fechaMasUno = fecha.AddDays(1);
            return db.Inventarios.Where(i => i.Empresa == empresa && i.Almacén == almacen && i.Fecha >= fecha && i.Fecha < fechaMasUno && i.Estado == ESTADO_INVENTARIO_SIN_CONTABILIZAR).OrderByDescending(i => i.Fecha);
        }

        // GET: api/Inventarios?empresa=1&almacen=ALG&fecha=11/21/2026&producto=8435454004144
        // GET: api/Inventarios?empresa=1&almacen=ALG&fecha=11/21/2026&producto=8435454004144&pasillo=003&fila=001&columna=002
        /// <summary>
        /// La línea del día del producto leído (NestoAPI#788: la referencia o cualquiera de sus códigos de barras, igual que al
        /// recibir una reposición). 404 vacío si el producto aún no tiene línea ese día; 404 con Message si lo leído no es ningún
        /// producto; 409 con Message y Productos si el código lo llevan varios.
        /// </summary>
        /// <param name="pasillo">
        /// NestoAPI#795: en almacenes con ubicaciones, la línea de esa ubicación (el mismo producto puede estar en varias). Pasillo, fila
        /// y columna van juntos: si viene alguno, se buscan los tres (el que falte, sin ubicación). Sin ninguno, como siempre.
        /// </param>
        [ResponseType(typeof(Inventario))]
        public async Task<IHttpActionResult> GetInventario(string empresa, string almacen, DateTime fecha, string producto,
            string pasillo = null, string fila = null, string columna = null)
        {
            ResultadoProductoLeido leido = await lector.Leer(empresa, producto).ConfigureAwait(false);
            if (leido.Producto == null)
            {
                return RespuestaSinProducto(leido);
            }
            Producto productoEncontrado = leido.Producto;

            DateTime fechaMasUno = fecha.AddDays(1);
            // Un producto que aún no se ha contado ese día no tiene línea: es un 404 (Nesto lo trata
            // como «no hay línea»), no un 500 de SingleAsync que ensucie ELMAH.
            IQueryable<Inventario> consulta = db.Inventarios.Where(i => i.Empresa == empresa && i.Almacén == almacen && i.Fecha >= fecha && i.Fecha < fechaMasUno && i.Número == productoEncontrado.Número && i.Estado == ESTADO_INVENTARIO_SIN_CONTABILIZAR);
            if (!string.IsNullOrWhiteSpace(pasillo) || !string.IsNullOrWhiteSpace(fila) || !string.IsNullOrWhiteSpace(columna))
            {
                // NestoAPI#795: la columna es char(3), así que en SQL «003» casa con «003 »; aquí solo se quitan los espacios de lo pedido.
                string p = string.IsNullOrWhiteSpace(pasillo) ? null : pasillo.Trim();
                string f = string.IsNullOrWhiteSpace(fila) ? null : fila.Trim();
                string c = string.IsNullOrWhiteSpace(columna) ? null : columna.Trim();
                consulta = consulta.Where(i => i.Pasillo == p && i.Fila == f && i.Columna == c);
            }
            Inventario inventario = await consulta.SingleOrDefaultAsync();
            if (inventario == null)
            {
                return NotFound();
            }

            return Ok(inventario);
        }

        // GET: api/Inventarios/Pendientes?empresa=1&almacen=REI&fecha=11/21/2026
        /// <summary>
        /// NestoAPI#788: lo pendiente de contabilizar (Estado 1 y sin NºTraspaso) del almacén, por usuario y día: filas, unidades
        /// contadas y calculadas, y cuántas filas se leyeron por código, cuántas se teclearon y cuántas no lo dicen. Sin fecha,
        /// todas las pendientes del almacén.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Inventarios/Pendientes")]
        [ResponseType(typeof(List<InventarioPendienteDTO>))]
        public async Task<IHttpActionResult> GetPendientes(string almacen, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, DateTime? fecha = null)
        {
            if (string.IsNullOrWhiteSpace(almacen))
            {
                return BadRequest("Falta el almacén.");
            }
            empresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            almacen = almacen.Trim();
            IQueryable<Inventario> consulta = db.Inventarios.Where(i => i.Empresa == empresa && i.Almacén == almacen
                && i.Estado == ESTADO_INVENTARIO_SIN_CONTABILIZAR && i.NºTraspaso == null);
            if (fecha.HasValue)
            {
                DateTime desde = fecha.Value.Date;
                DateTime hasta = desde.AddDays(1);
                consulta = consulta.Where(i => i.Fecha >= desde && i.Fecha < hasta);
            }
            var filas = await consulta
                .Select(i => new { i.Usuario, i.Fecha, i.StockReal, i.StockCalculado, i.PorCodigoBarras })
                .ToListAsync().ConfigureAwait(false);

            List<InventarioPendienteDTO> resumen = filas
                // NestoAPI#827: una persona, un renglón, aunque su usuario esté grabado con varias cajas.
                .GroupBy(f => new { Usuario = (f.Usuario?.Trim() ?? string.Empty).ToUpperInvariant(), Fecha = f.Fecha.Date })
                .Select(g => new InventarioPendienteDTO
                {
                    Usuario = NestoAPI.Infraestructure.UsuarioAuditoriaHelper.ParaMostrar(g.First().Usuario),
                    Fecha = g.Key.Fecha,
                    Filas = g.Count(),
                    StockReal = g.Sum(f => f.StockReal ?? 0),
                    StockCalculado = g.Sum(f => f.StockCalculado ?? 0),
                    FilasPorCodigoBarras = g.Count(f => f.PorCodigoBarras == true),
                    FilasTecleadas = g.Count(f => f.PorCodigoBarras == false),
                    FilasSinMarca = g.Count(f => f.PorCodigoBarras == null)
                })
                .OrderBy(r => r.Fecha)
                .ThenBy(r => r.Usuario, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Ok(resumen);
        }

        // PUT: api/Inventarios/5?porCodigoBarras=false
        /// <param name="porCodigoBarras">
        /// NestoAPI#788: cómo se ha leído esta vez. False (tecleada la referencia) deja la fila a 0 para siempre; true la deja a 1
        /// solo si no tenía marca. Sin él no se toca lo que hubiera (lo que traiga el cuerpo se ignora: Nesto manda la línea entera).
        /// </param>
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutInventario(int id, Inventario inventario, bool? porCodigoBarras = null)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != inventario.NºOrden)
            {
                return BadRequest();
            }

            bool? marcaGuardada = (await db.Inventarios.Where(i => i.NºOrden == id).Select(i => i.PorCodigoBarras).ToListAsync().ConfigureAwait(false))
                .FirstOrDefault();
            inventario.PorCodigoBarras = MarcaTrasSumar(marcaGuardada, porCodigoBarras);

            db.Entry(inventario).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!InventarioExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return StatusCode(HttpStatusCode.NoContent);
        }

        // POST: api/Inventarios
        /// <summary>
        /// Una línea nueva. En Número puede venir la referencia o un código de barras (NestoAPI#788, la regla de la recepción);
        /// se graba la referencia y PorCodigoBarras (1 código, 0 referencia tecleada). 404 / 409 como el GET.
        /// </summary>
        [ResponseType(typeof(Inventario))]
        public async Task<IHttpActionResult> PostInventario(Inventario inventario)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (string.IsNullOrWhiteSpace(inventario.Empresa) || string.IsNullOrWhiteSpace(inventario.Número))
            {
                throw new Exception("Es obligatorio especificar el producto");
            }

            ResultadoProductoLeido leido = await lector.Leer(inventario.Empresa, inventario.Número).ConfigureAwait(false);
            if (leido.Producto == null)
            {
                return RespuestaSinProducto(leido);
            }
            Producto producto = leido.Producto;

            inventario.Número = producto.Número;
            inventario.PorCodigoBarras = leido.PorCodigoBarras;
            inventario.Descripción = inventario.Descripción ?? producto.Nombre;
            inventario.Familia = inventario.Familia ?? producto.Familia;
            inventario.Grupo = inventario.Grupo ?? producto.Grupo;
            inventario.Subgrupo = inventario.Subgrupo ?? producto.SubGrupo;
            inventario.Estado = 1; // Sin contabilizar
            inventario.Aplicacion = "NestoAPI";
            // #481: Usuario ya no es Computed en el EDMX (antes el DEFAULT grababa el pool de IIS).
            // Manda el Identity; lo que traiga el cliente es el fallback (Nesto manda configuracion.usuario).
            inventario.Usuario = Infraestructure.UsuarioAuditoriaHelper.ParaAuditoria(
                Infraestructure.UsuarioAuditoriaHelper.Resolver(User, inventario.Usuario));

            db.Inventarios.Add(inventario);
            
            await db.SaveChangesAsync();

            return CreatedAtRoute("DefaultApi", new { id = inventario.NºOrden }, inventario);
        }
        /*
        // DELETE: api/Inventarios/5
        [ResponseType(typeof(Inventario))]
        public async Task<IHttpActionResult> DeleteInventario(int id)
        {
            Inventario inventario = await db.Inventarios.FindAsync(id);
            if (inventario == null)
            {
                return NotFound();
            }

            db.Inventarios.Remove(inventario);
            await db.SaveChangesAsync();

            return Ok(inventario);
        }
        */
        

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool InventarioExists(int id)
        {
            return db.Inventarios.Count(e => e.NºOrden == id) > 0;
        }

        /// <summary>NestoAPI#788: una lectura tecleada deja la fila a 0 para siempre; una por código solo marca la que no tenía marca.</summary>
        internal static bool? MarcaTrasSumar(bool? guardada, bool? estaLectura)
        {
            if (estaLectura == false)
            {
                return false;
            }
            if (estaLectura == true)
            {
                return guardada ?? true;
            }
            return guardada;
        }

        private IHttpActionResult RespuestaSinProducto(ResultadoProductoLeido leido)
        {
            if (leido.EsAmbiguo)
            {
                return Content(HttpStatusCode.Conflict, new ProductoLeidoInventarioDTO { Message = leido.MensajeAmbiguo, Productos = leido.Candidatos });
            }
            return Content(HttpStatusCode.NotFound, new ProductoLeidoInventarioDTO
            {
                Message = leido.Leido == null ? "Falta la referencia o el código de barras del producto." : leido.MensajeNoEncontrado
            });
        }
    }
}