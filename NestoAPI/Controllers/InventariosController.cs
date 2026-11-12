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
            HuecosInventario = new Infraestructure.Inventarios.LectorHuecoInventarioSql(db);
        }

        /// <summary>Incidencia 1227: si el almacén va por huecos y lo libre del hueco (sustituible en los tests).</summary>
        internal Infraestructure.Inventarios.ILectorHuecoInventario HuecosInventario { get; set; }

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
        /// producto; 409 con Message y Productos si el código lo llevan varios. NestoAPI#784: si el producto tiene más de una fila pendiente ese
        /// día (en esa ubicación, si viene), 409 con Codigo «FilasDuplicadas» y las Lineas (antes, 500 y Nesto creaba otra más).
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
            // NestoAPI#784 (09/11/26): con dos filas pendientes, SingleOrDefault daba 500 y Nesto, al no ver la línea, creaba otra.
            List<Inventario> filas = await consulta.OrderBy(i => i.NºOrden).Take(MAXIMO_FILAS_EN_CONFLICTO).ToListAsync().ConfigureAwait(false);
            if (filas.Count == 0)
            {
                return NotFound();
            }
            List<string> ubicaciones = filas.Select(UbicacionDe).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (filas.Count > 1 && ubicaciones.Count > 1)
            {
                // Algete sin ubicación: el producto está en varias (no es un duplicado), hay que decir cuál.
                return Content(HttpStatusCode.Conflict, new YaRellenadoInventarioDTO
                {
                    Codigo = Infraestructure.Inventarios.CodigosConflictoInventario.VARIAS_UBICACIONES,
                    Message = $"El {productoEncontrado.Número?.Trim()} tiene {filas.Count} filas sin contabilizar el {Dia(fecha)} en {almacen?.Trim()}, " +
                        $"en ubicaciones distintas ({string.Join(", ", ubicaciones.Select(u => u.Length == 0 ? "sin ubicación" : u))}): indique la ubicación.",
                    Lineas = filas.Select(Linea).ToList()
                });
            }
            if (filas.Count > 1)
            {
                return Content(HttpStatusCode.Conflict, new YaRellenadoInventarioDTO
                {
                    Codigo = Infraestructure.Inventarios.CodigosConflictoInventario.FILAS_DUPLICADAS,
                    Message = $"El {productoEncontrado.Número?.Trim()} tiene {filas.Count} filas sin contabilizar el {Dia(fecha)} en {almacen?.Trim()}" +
                        $"{TextoUbicacion(filas[0])} ({Detalle(filas)}). Hay que dejar solo una antes de seguir contando: avise a Informática.",
                    Lineas = filas.Select(Linea).ToList()
                });
            }

            return Ok(filas[0]);
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

            var guardada = (await db.Inventarios.Where(i => i.NºOrden == id).Select(i => new { i.PorCodigoBarras, i.Pasillo, i.Fila, i.Columna })
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();
            bool? marcaGuardada = guardada?.PorCodigoBarras;
            // Incidencia 1227: una fila que tenía hueco no se queda sin él por un PUT (sería «sin ubicación» y bloquearía el contabilizar)
            if (guardada != null && Infraestructure.Inventarios.ReglasHuecoInventario.TraeHueco(guardada.Pasillo, guardada.Fila, guardada.Columna)
                && !Infraestructure.Inventarios.ReglasHuecoInventario.TraeHueco(inventario.Pasillo, inventario.Fila, inventario.Columna))
            {
                return Content(HttpStatusCode.BadRequest, new { Message = Infraestructure.Inventarios.ReglasHuecoInventario.MensajeSinHueco(inventario.Almacén) });
            }
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
        /// se graba la referencia y PorCodigoBarras (1 código, 0 referencia tecleada). 404 / 409 como el GET. NestoAPI#784: 409 con
        /// Codigo «YaTieneFila» y las Lineas si el producto ya tiene fila pendiente ese día en ese almacén y ubicación (sin ubicación,
        /// las que no la tienen): se suma a esa con el PUT.
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

            // NestoAPI#784 (09/11/26): si el producto ya tiene fila pendiente ese día en ese almacén (y ubicación), no se crea otra: el
            // SP apuntaría la diferencia de cada una. Lo contado se suma a la que hay (PUT).
            DateTime dia = inventario.Fecha.Date;
            DateTime diaMasUno = dia.AddDays(1);
            string pasillo = string.IsNullOrWhiteSpace(inventario.Pasillo) ? null : inventario.Pasillo.Trim();
            string fila = string.IsNullOrWhiteSpace(inventario.Fila) ? null : inventario.Fila.Trim();
            string columna = string.IsNullOrWhiteSpace(inventario.Columna) ? null : inventario.Columna.Trim();
            string numero = producto.Número;

            // Incidencia 1227: en un almacén con ubicaciones, un producto que se ubica va por huecos (sin hueco, la fila bloquea el
            // contabilizar y, de un día para otro, el picking). Con hueco, el teórico es lo libre de ese producto en ese hueco.
            if (HuecosInventario != null && producto.Ubicar
                && await HuecosInventario.ControlUbicaciones(inventario.Empresa, inventario.Almacén).ConfigureAwait(false))
            {
                if (!Infraestructure.Inventarios.ReglasHuecoInventario.TraeHueco(pasillo, fila, columna))
                {
                    return Content(HttpStatusCode.BadRequest, new { Message = Infraestructure.Inventarios.ReglasHuecoInventario.MensajeSinHueco(inventario.Almacén) });
                }
                inventario.StockCalculado = await HuecosInventario.LibreEnHueco(inventario.Almacén, numero, pasillo, fila, columna).ConfigureAwait(false);
            }

            List<Inventario> yaHay = await db.Inventarios
                .Where(i => i.Empresa == inventario.Empresa && i.Almacén == inventario.Almacén && i.Fecha >= dia && i.Fecha < diaMasUno
                    && i.Número == numero && i.Estado == ESTADO_INVENTARIO_SIN_CONTABILIZAR
                    && i.Pasillo == pasillo && i.Fila == fila && i.Columna == columna)
                .OrderBy(i => i.NºOrden).Take(MAXIMO_FILAS_EN_CONFLICTO)
                .ToListAsync().ConfigureAwait(false);
            if (yaHay.Count > 0)
            {
                string cuantas = yaHay.Count == 1 ? "una fila" : $"{yaHay.Count} filas";
                return Content(HttpStatusCode.Conflict, new YaRellenadoInventarioDTO
                {
                    Codigo = Infraestructure.Inventarios.CodigosConflictoInventario.YA_TIENE_FILA,
                    Message = $"El {numero?.Trim()} ya tiene {cuantas} sin contabilizar el {Dia(dia)} en {inventario.Almacén?.Trim()}{TextoUbicacion(yaHay[0])} " +
                        $"({Detalle(yaHay)}): sume lo contado a esa fila en vez de crear otra.",
                    Lineas = yaHay.Select(Linea).ToList()
                });
            }

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

        /// <summary>NestoAPI#784: cuántas filas se enseñan, como mucho, en un 409 de filas duplicadas.</summary>
        internal const int MAXIMO_FILAS_EN_CONFLICTO = 20;

        private static string Dia(DateTime fecha) => fecha.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);

        private static string UbicacionDe(Inventario fila) =>
            string.IsNullOrWhiteSpace(fila.Pasillo) && string.IsNullOrWhiteSpace(fila.Fila) && string.IsNullOrWhiteSpace(fila.Columna)
                ? string.Empty
                : $"{fila.Pasillo?.Trim()}-{fila.Fila?.Trim()}-{fila.Columna?.Trim()}";

        private static string TextoUbicacion(Inventario fila) =>
            string.IsNullOrWhiteSpace(fila.Pasillo) ? string.Empty : $", ubicación {fila.Pasillo?.Trim()}-{fila.Fila?.Trim()}-{fila.Columna?.Trim()}";

        /// <summary>«nº 948780: contado 2, Paloma, 10:50; nº 948790: contado 0, Paloma, 11:02».</summary>
        private static string Detalle(IEnumerable<Inventario> filas) => string.Join("; ", filas.Select(f =>
            $"nº {f.NºOrden}: contado {f.StockReal ?? 0}, {SinDominio(f.Usuario)}, {f.Fecha_Modificación.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)}"));

        private static string SinDominio(string usuario)
        {
            string limpio = usuario?.Trim() ?? string.Empty;
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 ? limpio.Substring(barra + 1) : limpio;
        }

        private static LineaInventarioContadaDTO Linea(Inventario fila) => new LineaInventarioContadaDTO
        {
            NumeroOrden = fila.NºOrden,
            Producto = fila.Número?.Trim(),
            Descripcion = fila.Descripción?.Trim(),
            Familia = fila.Familia?.Trim(),
            Pasillo = fila.Pasillo?.Trim(),
            Fila = fila.Fila?.Trim(),
            Columna = fila.Columna?.Trim(),
            StockCalculado = fila.StockCalculado ?? 0,
            StockReal = fila.StockReal ?? 0,
            Usuario = fila.Usuario?.Trim(),
            Fecha = fila.Fecha,
            FechaModificacion = fila.Fecha_Modificación,
            PorCodigoBarras = fila.PorCodigoBarras,
            Traspaso = fila.NºTraspaso
        };

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