using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Kits;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    [RoutePrefix("api/PrestashopProductos")]
    public class PrestashopProductosController : ApiController
    {
        private readonly NVEntities db;
        private readonly IGestorProductos _gestorProductos;
        private readonly IProductoService _productoService;
        private readonly PublicacionTienda _publicacionTienda;
        public PrestashopProductosController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            _gestorProductos = new GestorProductos(new SincronizacionEventWrapper(new GooglePubSubEventPublisher()));
            _productoService = new ProductoService();
            _publicacionTienda = new PublicacionTienda(_gestorProductos);
        }

        /// <param name="publicacionTienda">NestoAPI#701: por defecto, el gestor con reintentos en Hangfire si Pub/Sub falla.</param>
        public PrestashopProductosController(NVEntities db, IGestorProductos gestorProductos = null, IProductoService productoService = null,
            PublicacionTienda publicacionTienda = null)
        {
            this.db = db;
            this.db.Configuration.LazyLoadingEnabled = false;
            _gestorProductos = gestorProductos;
            _productoService = productoService;
            _publicacionTienda = publicacionTienda ?? (gestorProductos == null ? null : new PublicacionTienda(gestorProductos));
        }

        // GET: api/PrestashopProductos/17404
        [Route("{productoId}")]
        [ResponseType(typeof(PrestashopProductoDTO))]
        public async Task<IHttpActionResult> GetPrestashopProducto(string productoId)
        {
            var producto = await db.PrestashopProductos
                .FirstOrDefaultAsync(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == productoId);

            if (producto == null)
            {
                return NotFound();
            }

            return Ok(MapToDTO(producto));
        }

        // GET: api/PrestashopProductos?sinVistoBueno=true
        [Route("")]
        [ResponseType(typeof(List<PrestashopProductoDTO>))]
        public async Task<IHttpActionResult> GetPrestashopProductos(bool sinVistoBueno = false)
        {
            IQueryable<PrestashopProducto> query = db.PrestashopProductos
                .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO);

            if (sinVistoBueno)
            {
                query = query.Where(p => p.VistoBueno == null || p.VistoBueno == false);
            }

            var productos = await query
                .Select(p => new PrestashopProductoDTO
                {
                    ProductoId = p.Número,
                    Nombre = p.Nombre,
                    DescripcionBreve = p.DescripciónBreve,
                    DescripcionCompleta = p.Descripción,
                    PvpIvaIncluido = p.PVP_IVA_Incluido,
                    VistoBueno = p.VistoBueno ?? false
                })
                .ToListAsync();

            return Ok(productos);
        }

        // POST: api/PrestashopProductos
        [Route("")]
        [ResponseType(typeof(PrestashopProductoDTO))]
        public async Task<IHttpActionResult> PostPrestashopProducto(PrestashopProductoDTO dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.ProductoId))
            {
                return BadRequest("ProductoId es obligatorio");
            }

            var existente = await db.PrestashopProductos
                .FirstOrDefaultAsync(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == dto.ProductoId);

            if (existente != null)
            {
                return Conflict();
            }

            var producto = new PrestashopProducto
            {
                Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Número = dto.ProductoId,
                Nombre = dto.Nombre,
                DescripciónBreve = dto.DescripcionBreve,
                Descripción = dto.DescripcionCompleta,
                PVP_IVA_Incluido = dto.PvpIvaIncluido,
                VistoBueno = dto.VistoBueno,
                Usuario = User?.Identity?.Name,
                Fecha_Modificación = DateTime.Now
            };

            db.PrestashopProductos.Add(producto);
            await db.SaveChangesAsync();

            await PublicarProductoEnNestoSync(dto.ProductoId);

            string location = Url.Content($"~/api/PrestashopProductos/{dto.ProductoId}");
            return Created(location, MapToDTO(producto));
        }

        // PUT: api/PrestashopProductos
        [Route("")]
        [ResponseType(typeof(PrestashopProductoDTO))]
        public async Task<IHttpActionResult> PutPrestashopProducto(PrestashopProductoDTO dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.ProductoId))
            {
                return BadRequest("ProductoId es obligatorio");
            }

            var producto = await db.PrestashopProductos
                .FirstOrDefaultAsync(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == dto.ProductoId);

            if (producto == null)
            {
                return NotFound();
            }

            producto.Nombre = dto.Nombre;
            producto.DescripciónBreve = dto.DescripcionBreve;
            producto.Descripción = dto.DescripcionCompleta;
            producto.PVP_IVA_Incluido = dto.PvpIvaIncluido;
            producto.VistoBueno = dto.VistoBueno;
            producto.Usuario = User?.Identity?.Name;
            producto.Fecha_Modificación = DateTime.Now;

            await db.SaveChangesAsync();

            // NestoAPI#701: lo guardado ya está hecho. Si Pub/Sub falla, la respuesta sigue siendo 200 con un
            // aviso y el reintento queda programado en Hangfire; el usuario no tiene que volver a guardar.
            string aviso = await PublicarProductoEnNestoSync(dto.ProductoId);

            PrestashopProductoDTO respuesta = MapToDTO(producto);
            respuesta.Aviso = aviso;
            return Ok(respuesta);
        }

        /// <returns>El aviso para el usuario si la tienda se actualizará más tarde; null si se publicó (o no había que publicar).</returns>
        private async Task<string> PublicarProductoEnNestoSync(string productoId)
        {
            if (_publicacionTienda == null)
            {
                return null;
            }

            // NestoAPI#432: también aquí decide la puerta de publicación. Si el producto no debe
            // estar en la tienda, guardar sus textos no lo publica.
            ResultadoPuertaPublicacion resultado = await PublicacionTienda
                .ConstruirPorLaPuerta(productoId, db, _productoService).ConfigureAwait(false);
            if (resultado == null)
            {
                return null;
            }
            if (!resultado.Publicable)
            {
                Console.WriteLine($"⛔ Producto {productoId} no publicable: {resultado.Motivo}");
                return null;
            }

            string usuario = User?.Identity?.Name;
            ResultadoPublicacionTienda publicacion = await _publicacionTienda
                .PublicarOAplazar(resultado.Dto, "Nesto", usuario).ConfigureAwait(false);
            return publicacion.Aviso;
        }

        private static PrestashopProductoDTO MapToDTO(PrestashopProducto producto)
        {
            return new PrestashopProductoDTO
            {
                ProductoId = producto.Número?.Trim(),
                Nombre = producto.Nombre,
                DescripcionBreve = producto.DescripciónBreve,
                DescripcionCompleta = producto.Descripción,
                PvpIvaIncluido = producto.PVP_IVA_Incluido,
                VistoBueno = producto.VistoBueno ?? false
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
