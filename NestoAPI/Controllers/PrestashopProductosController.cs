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
        private readonly SincronizacionEventWrapper _sincronizacionEventWrapper;

        public PrestashopProductosController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            var eventWrapper = new SincronizacionEventWrapper(new GooglePubSubEventPublisher());
            _gestorProductos = new GestorProductos(eventWrapper);
            _productoService = new ProductoService();
            _sincronizacionEventWrapper = eventWrapper;
        }

        public PrestashopProductosController(NVEntities db, IGestorProductos gestorProductos = null, IProductoService productoService = null, SincronizacionEventWrapper sincronizacionEventWrapper = null)
        {
            this.db = db;
            this.db.Configuration.LazyLoadingEnabled = false;
            _gestorProductos = gestorProductos;
            _productoService = productoService;
            _sincronizacionEventWrapper = sincronizacionEventWrapper;
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
            await PublicarPrestashopProductoSync(producto);

            return CreatedAtRoute("DefaultApi", new { id = dto.ProductoId }, MapToDTO(producto));
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

            await PublicarProductoEnNestoSync(dto.ProductoId);
            await PublicarPrestashopProductoSync(producto);

            return Ok(MapToDTO(producto));
        }

        private async Task PublicarProductoEnNestoSync(string productoId)
        {
            if (_gestorProductos == null)
            {
                return;
            }

            var productoEntity = await db.Productos
                .Include(p => p.Kits)
                .Include(p => p.Familia1)
                .Include(p => p.SubGruposProducto)
                .SingleOrDefaultAsync(p => p.Número == productoId && p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO);

            if (productoEntity == null)
            {
                return;
            }

            var productoDTO = new ProductoDTO
            {
                UrlFoto = await ProductoDTO.RutaImagen(productoId).ConfigureAwait(false),
                PrecioPublicoFinal = await ProductoDTO.LeerPrecioPublicoFinal(productoId, db).ConfigureAwait(false),
                UrlEnlace = await ProductoDTO.RutaEnlace(productoId).ConfigureAwait(false),
                Producto = productoEntity.Número?.Trim(),
                Nombre = productoEntity.Nombre?.Trim(),
                Tamanno = productoEntity.Tamaño,
                UnidadMedida = productoEntity.UnidadMedida?.Trim(),
                Familia = productoEntity.Familia1?.Descripción?.Trim(),
                PrecioProfesional = (decimal)productoEntity.PVP,
                Estado = (short)productoEntity.Estado,
                Grupo = productoEntity.Grupo,
                Subgrupo = productoEntity.SubGruposProducto?.Descripción?.Trim(),
                RoturaStockProveedor = productoEntity.RoturaStockProveedor,
                CodigoBarras = productoEntity.CodBarras?.Trim()
            };

            foreach (var kit in productoEntity.Kits)
            {
                productoDTO.ProductosKit.Add(new ProductoKit
                {
                    ProductoId = kit.NúmeroAsociado.Trim(),
                    Cantidad = kit.Cantidad
                });
            }

            if (_productoService != null && !productoEntity.Ficticio)
            {
                productoDTO.Stocks.Add(await _productoService.CalcularStockProducto(productoId, Constantes.Almacenes.ALGETE));
                productoDTO.Stocks.Add(await _productoService.CalcularStockProducto(productoId, Constantes.Almacenes.REINA));
                productoDTO.Stocks.Add(await _productoService.CalcularStockProducto(productoId, Constantes.Almacenes.ALCOBENDAS));
            }

            string usuario = User?.Identity?.Name;
            await _gestorProductos.PublicarProductoSincronizar(productoDTO, "Nesto", usuario);
        }

        private async Task PublicarPrestashopProductoSync(PrestashopProducto producto)
        {
            if (_sincronizacionEventWrapper == null)
            {
                return;
            }

            var message = new PrestashopProductoSyncMessage
            {
                Tabla = "PrestashopProductos",
                Source = "Nesto",
                Usuario = User?.Identity?.Name,
                Producto = producto.Número?.Trim(),
                NombrePersonalizado = string.IsNullOrWhiteSpace(producto.Nombre) ? null : producto.Nombre.Trim(),
                Descripcion = producto.Descripción,
                DescripcionBreve = producto.DescripciónBreve,
                PVP_IVA_Incluido = producto.PVP_IVA_Incluido
            };

            await _sincronizacionEventWrapper.PublishSincronizacionEventAsync("sincronizacion-tablas", message);
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
