using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Models.OfertasCombinadas;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Mantenimiento de las ofertas permitidas de un PRODUCTO: los "6+2" de toda la vida.
    ///
    /// Hasta ahora solo se metían desde Nesto viejo, y allí no se les puede poner fecha porque la
    /// tabla no tenía columnas de fecha: apagar una oferta era borrar la fila y acordarse. Caso que
    /// lo motivó: la petición por correo del 31/08/2026 de poner el 6+2 del producto 44724 — el
    /// mismo producto que ese día dejó dos errores en ELMAH ("No se encuentra autorización para la
    /// oferta del producto 44724") de pedidos rebotando antes de que la oferta existiera.
    ///
    /// Gestiona SOLO las ofertas generales. Las de un cliente concreto se quedan fuera a propósito:
    /// son otra cosa y su sitio es la ficha de ese cliente (decisión de Carlos, 31/08/2026). Si se
    /// colaran aquí, se podría borrar desde una pantalla general un acuerdo con un cliente.
    ///
    /// La vigencia la respeta <see cref="ServicioPrecios.BuscarOfertasPermitidas"/>, que es el
    /// único punto de lectura de la tabla: una oferta con la fecha pasada deja de autorizar el
    /// pedido. No hace falta republicar nada — esta tabla no viaja a la tienda, autoriza pedidos.
    /// </summary>
    [Authorize]
    public class OfertasPermitidasProductoController : ApiController
    {
        private readonly NVEntities db;

        public OfertasPermitidasProductoController()
        {
            db = new NVEntities();
        }

        public OfertasPermitidasProductoController(NVEntities context)
        {
            db = context;
        }

        /// <param name="incluirCaducadas">Las que ya pasaron su FechaHasta. Por defecto no salen.</param>
        [HttpGet]
        [Route("api/OfertasPermitidasProducto")]
        [ResponseType(typeof(List<OfertaPermitidaProductoDTO>))]
        public async Task<IHttpActionResult> GetOfertasPermitidasProducto(bool incluirCaducadas = false)
        {
            DateTime hoy = DateTime.Today;

            List<OfertaPermitida> ofertas = await Generales().OrderBy(o => o.Número).ToListAsync().ConfigureAwait(false);

            if (!incluirCaducadas)
            {
                ofertas = ofertas.Where(o => o.FechaHasta == null || o.FechaHasta >= hoy).ToList();
            }

            Dictionary<string, string> nombres = await NombresDe(ofertas).ConfigureAwait(false);
            return Ok(ofertas.Select(o => ADto(o, nombres, hoy)).ToList());
        }

        [HttpPost]
        [Route("api/OfertasPermitidasProducto")]
        [ResponseType(typeof(OfertaPermitidaProductoDTO))]
        public async Task<IHttpActionResult> PostOfertaPermitidaProducto([FromBody] OfertaPermitidaProductoCreateDTO dto)
        {
            string error = await Validar(dto, null).ConfigureAwait(false);
            if (error != null)
            {
                return BadRequest(error);
            }

            OfertaPermitida oferta = new OfertaPermitida
            {
                Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Número = dto.Producto.Trim(),
                CantidadConPrecio = dto.CantidadConPrecio,
                CantidadRegalo = dto.CantidadRegalo,
                Denegar = dto.Denegar,
                FiltroProducto = string.IsNullOrWhiteSpace(dto.FiltroProducto) ? null : dto.FiltroProducto.Trim(),
                FechaDesde = dto.FechaDesde,
                FechaHasta = dto.FechaHasta,
                Usuario = UsuarioAuditoriaHelper.Resolver(User, null),
                FechaModificación = DateTime.Now
            };

            _ = db.OfertasPermitidas.Add(oferta);
            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            return Ok(ADto(oferta, await NombresDe(new List<OfertaPermitida> { oferta }).ConfigureAwait(false), DateTime.Today));
        }

        [HttpPut]
        [Route("api/OfertasPermitidasProducto/{nOrden:int}")]
        [ResponseType(typeof(OfertaPermitidaProductoDTO))]
        public async Task<IHttpActionResult> PutOfertaPermitidaProducto(int nOrden, [FromBody] OfertaPermitidaProductoCreateDTO dto)
        {
            OfertaPermitida oferta = await Generales().FirstOrDefaultAsync(o => o.NºOrden == nOrden).ConfigureAwait(false);
            if (oferta == null)
            {
                return NotFound();
            }

            string error = await Validar(dto, nOrden).ConfigureAwait(false);
            if (error != null)
            {
                return BadRequest(error);
            }

            oferta.Número = dto.Producto.Trim();
            oferta.CantidadConPrecio = dto.CantidadConPrecio;
            oferta.CantidadRegalo = dto.CantidadRegalo;
            oferta.Denegar = dto.Denegar;
            oferta.FiltroProducto = string.IsNullOrWhiteSpace(dto.FiltroProducto) ? null : dto.FiltroProducto.Trim();
            oferta.FechaDesde = dto.FechaDesde;
            oferta.FechaHasta = dto.FechaHasta;
            oferta.Usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            oferta.FechaModificación = DateTime.Now;

            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            return Ok(ADto(oferta, await NombresDe(new List<OfertaPermitida> { oferta }).ConfigureAwait(false), DateTime.Today));
        }

        [HttpDelete]
        [Route("api/OfertasPermitidasProducto/{nOrden:int}")]
        public async Task<IHttpActionResult> DeleteOfertaPermitidaProducto(int nOrden)
        {
            OfertaPermitida oferta = await Generales().FirstOrDefaultAsync(o => o.NºOrden == nOrden).ConfigureAwait(false);
            if (oferta == null)
            {
                return NotFound();
            }

            _ = db.OfertasPermitidas.Remove(oferta);
            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            return Ok();
        }

        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Las ofertas de producto GENERALES. El filtro de Cliente es lo que impide que desde esta
        /// pantalla se toque un acuerdo con un cliente concreto.
        /// </summary>
        private IQueryable<OfertaPermitida> Generales()
        {
            return db.OfertasPermitidas.Where(o => o.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                && o.Cliente == null
                && o.Número != null);
        }

        private async Task<Dictionary<string, string>> NombresDe(List<OfertaPermitida> ofertas)
        {
            List<string> numeros = ofertas.Select(o => o.Número).Distinct().ToList();
            if (!numeros.Any())
            {
                return new Dictionary<string, string>();
            }

            var filas = await db.Productos
                .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && numeros.Contains(p.Número))
                .Select(p => new { p.Número, p.Nombre })
                .ToListAsync().ConfigureAwait(false);

            // El diccionario va con la clave RECORTADA porque Número es char(15) y los valores que
            // llegan de una y otra tabla pueden traer relleno distinto.
            return filas.GroupBy(f => f.Número.Trim())
                        .ToDictionary(g => g.Key, g => g.First().Nombre?.Trim());
        }

        private async Task<string> Validar(OfertaPermitidaProductoCreateDTO dto, int? idQueSeEdita)
        {
            if (dto == null)
            {
                return "No ha llegado ninguna oferta";
            }
            if (string.IsNullOrWhiteSpace(dto.Producto))
            {
                return "El producto es obligatorio";
            }
            if (dto.CantidadConPrecio < 1)
            {
                return "La cantidad con precio tiene que ser al menos 1";
            }
            if (dto.CantidadRegalo < 1)
            {
                return "La cantidad de regalo tiene que ser al menos 1";
            }
            if (dto.FechaDesde.HasValue && dto.FechaHasta.HasValue && dto.FechaDesde.Value > dto.FechaHasta.Value)
            {
                return "La fecha de inicio no puede ser posterior a la de fin";
            }

            string producto = dto.Producto.Trim();
            bool existe = await db.Productos
                .AnyAsync(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == producto)
                .ConfigureAwait(false);
            if (!existe)
            {
                return $"El producto {producto} no existe";
            }

            return await ValidarDuplicada(dto, idQueSeEdita).ConfigureAwait(false);
        }

        /// <summary>
        /// Dos ofertas del mismo producto y mismo filtro vigentes A LA VEZ son ambiguas: el
        /// validador de pedidos las recorre y aplicaría una cualquiera. Encadenarlas (una acaba, la
        /// siguiente empieza) sí vale, que es justo para lo que están las fechas.
        /// </summary>
        private async Task<string> ValidarDuplicada(OfertaPermitidaProductoCreateDTO dto, int? idQueSeEdita)
        {
            string producto = dto.Producto.Trim();
            string filtro = string.IsNullOrWhiteSpace(dto.FiltroProducto) ? null : dto.FiltroProducto.Trim();

            List<OfertaPermitida> mismas = await Generales().ToListAsync().ConfigureAwait(false);

            OfertaPermitida choca = mismas.FirstOrDefault(o =>
                o.NºOrden != (idQueSeEdita ?? 0)
                && o.Número?.Trim() == producto
                && (o.FiltroProducto?.Trim() ?? string.Empty) == (filtro ?? string.Empty)
                && SeSolapan(o.FechaDesde, o.FechaHasta, dto.FechaDesde, dto.FechaHasta));

            return choca == null
                ? null
                : $"Ya hay otra oferta del producto {producto} cuyas fechas se solapan (nº {choca.NºOrden}). " +
                  "Dos ofertas vigentes a la vez sobre el mismo producto dejan el pedido a merced de cuál se lea primero";
        }

        /// <summary>Dos rangos con extremos abiertos (null = sin límite) se solapan si cada uno
        /// empieza antes de que acabe el otro.</summary>
        internal static bool SeSolapan(DateTime? desdeA, DateTime? hastaA, DateTime? desdeB, DateTime? hastaB)
        {
            bool aEmpiezaAntesDeQueAcabeB = desdeA == null || hastaB == null || desdeA <= hastaB;
            bool bEmpiezaAntesDeQueAcabeA = desdeB == null || hastaA == null || desdeB <= hastaA;
            return aEmpiezaAntesDeQueAcabeB && bEmpiezaAntesDeQueAcabeA;
        }

        private static OfertaPermitidaProductoDTO ADto(OfertaPermitida oferta, Dictionary<string, string> nombres, DateTime hoy)
        {
            string producto = oferta.Número?.Trim();
            return new OfertaPermitidaProductoDTO
            {
                NOrden = oferta.NºOrden,
                Empresa = oferta.Empresa?.Trim(),
                Producto = producto,
                ProductoNombre = producto != null && nombres.ContainsKey(producto) ? nombres[producto] : null,
                CantidadConPrecio = oferta.CantidadConPrecio,
                CantidadRegalo = oferta.CantidadRegalo,
                Denegar = oferta.Denegar,
                FiltroProducto = oferta.FiltroProducto?.Trim(),
                FechaDesde = oferta.FechaDesde,
                FechaHasta = oferta.FechaHasta,
                Vigente = Vigencia.EsVigente(oferta, hoy),
                Usuario = oferta.Usuario?.Trim(),
                FechaModificacion = oferta.FechaModificación
            };
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
