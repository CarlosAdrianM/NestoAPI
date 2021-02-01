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
using NestoAPI.Models;
using NestoAPI.Infraestructure;

namespace NestoAPI.Controllers
{
    public class ProductosController : ApiController
    {
        private NVEntities db;

        public ProductosController()
        {
            db = new NVEntities();
        }

        public ProductosController(NVEntities db)
        {
            this.db = db;
        }

        /*
        // GET: api/Productos
        public IQueryable<Producto> GetProductos()
        {
            return db.Productos;
        }
        */

        // GET: api/Productos/5
        [ResponseType(typeof(ProductoPlantillaDTO))]
        public async Task<IHttpActionResult> GetProducto(string empresa, string id)
        {
            Producto producto = await db.Productos.SingleOrDefaultAsync(p => p.Empresa == empresa && p.Número == id);
            if (producto == null)
            {
                return NotFound();
            }

            ProductoPlantillaDTO productoDTO = new ProductoPlantillaDTO()
            {
                producto = producto.Número,
                nombre = producto.Nombre,
                precio = (decimal)producto.PVP,
                aplicarDescuento = producto.Aplicar_Dto
            };

            return Ok(productoDTO);
        }

        // GET: api/Productos/5
        [ResponseType(typeof(List<ClienteProductoDTO>))]
        public async Task<IHttpActionResult> GetProducto(string empresa, string id, string vendedor)
        {
            var lineasVenta = db.LinPedidoVtas.Include("Cliente").Where(l => l.Empresa == empresa && l.Producto == id);
            if (lineasVenta != null && !string.IsNullOrWhiteSpace(vendedor))
            {
                lineasVenta = lineasVenta.Where(l => l.Cliente.Vendedor == vendedor || 
                    (l.Cliente.VendedoresClienteGrupoProductoes.FirstOrDefault() != null && l.Cliente.VendedoresClienteGrupoProductoes.FirstOrDefault().Vendedor == vendedor)
                );
            }
            
            if (lineasVenta == null)
            {
                return NotFound();
            }

            var clienteProductoDTO = lineasVenta
                .Select(l => new ClienteProductoDTO
                {
                    Vendedor = l.Cliente.Vendedor.Trim(),
                    Cliente = l.Nº_Cliente.Trim(),
                    Contacto = l.Contacto.Trim(),
                    Nombre = l.Cliente.Nombre.Trim(),
                    Direccion = l.Cliente.Dirección.Trim(),
                    CodigoPostal = l.Cliente.CodPostal.Trim(),
                    Poblacion = l.Cliente.Población.Trim(),
                    Cantidad = (int)l.Cantidad,
                    EstadoMaximo = l.Estado,
                    EstadoMinimo = l.Estado,
                    UltimaCompra = l.Fecha_Modificación
                });

            clienteProductoDTO = clienteProductoDTO.GroupBy(g => new
            {
                g.Vendedor,
                g.Cliente,
                g.Contacto,
                g.Nombre,
                g.Direccion,
                g.CodigoPostal,
                g.Poblacion
            })
            .Select(x => new ClienteProductoDTO
            {
                Vendedor = x.Key.Vendedor,
                Cliente = x.Key.Cliente,
                Contacto = x.Key.Contacto,
                Nombre = x.Key.Nombre,
                Direccion = x.Key.Direccion,
                CodigoPostal = x.Key.CodigoPostal,
                Poblacion = x.Key.Poblacion,
                Cantidad = x.Sum(c => c.Cantidad),
                EstadoMaximo = x.Max(c => c.EstadoMaximo),
                EstadoMinimo = x.Min(c => c.EstadoMinimo),
                UltimaCompra = x.Max(c => c.UltimaCompra)
            });

            return Ok(clienteProductoDTO.OrderByDescending(c => c.Cantidad));
        }

        // GET: api/Productos/5
        [ResponseType(typeof(ProductoDTO))]
        public async Task<IHttpActionResult> GetProducto(string empresa, string id, bool fichaCompleta)
        {
            Producto producto = await db.Productos.SingleOrDefaultAsync(p => p.Empresa == empresa && p.Número == id);
            if (producto == null)
            {
                return NotFound();
            }

            ProductoDTO productoDTO = new ProductoDTO()
            {
                UrlFoto = await ProductoDTO.RutaImagen(id),
                Producto = producto.Número?.Trim(),
                Nombre = producto.Nombre?.Trim(),
                Tamanno = producto.Tamaño,
                UnidadMedida = producto.UnidadMedida?.Trim(),
                Familia = producto.Familia1.Descripción?.Trim(),
                PrecioProfesional = (decimal)producto.PVP,
                Estado = (short)producto.Estado,
                Grupo = producto.Grupo,
                Subgrupo = producto.SubGruposProducto.Descripción?.Trim()                
            };
            
            // Lo dejo medio-hardcoded porque no quiero que los vendedores vean otros almacenes
            if (!producto.Ficticio)
            {
                productoDTO.Stocks.Add(CalcularStockProducto(id, Constantes.Productos.ALMACEN_POR_DEFECTO));
                productoDTO.Stocks.Add(CalcularStockProducto(id, Constantes.Productos.ALMACEN_TIENDA));
            }            

            return Ok(productoDTO);
        }

        // GET: api/Productos/5
        public IQueryable<ProductoDTO> GetProducto(string empresa, string filtroNombre, string filtroFamilia, string filtroSubgrupo)
        {
            IQueryable<Producto> productos = db.Productos.Where(p => p.Empresa == empresa && p.Estado >= Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO);
            if (filtroNombre != null && filtroNombre.Trim() != "")
            {
                productos = productos.Where(p => p.Nombre.Contains(filtroNombre));
            }
            if (filtroFamilia != null && filtroFamilia.Trim() != "")
            {
                productos = productos.Where(p => p.Familia1.Descripción.Contains(filtroFamilia));
            }
            if (filtroSubgrupo != null && filtroSubgrupo?.Trim() != "")
            {
                productos = productos.Where(p => p.SubGruposProducto.Descripción.Contains(filtroSubgrupo));
            }

            var productosDTO = productos.Select(x=>new ProductoDTO
            {
                Producto = x.Número.Trim(),
                Nombre = x.Nombre.Trim(),
                Familia = x.Familia1.Descripción.Trim(),
                Subgrupo = x.SubGruposProducto.Descripción.Trim(),
                PrecioProfesional = (decimal)x.PVP,
                Estado = (short)x.Estado
            });

            return productosDTO;
        }

            

        // GET: api/Productos/5
        [ResponseType(typeof(ProductoPlantillaDTO))]
        public async Task<IHttpActionResult> GetProducto(string empresa, string id, string cliente, string contacto, short cantidad)
        {
            Producto producto = await db.Productos.SingleOrDefaultAsync(p => p.Empresa == empresa && p.Número == id);
            if (producto == null)
            {
                return NotFound();
            }

            ProductoPlantillaDTO productoDTO = new ProductoPlantillaDTO()
            {
                producto = producto.Número,
                nombre = producto.Nombre,
                precio = (decimal)producto.PVP,
                aplicarDescuento = producto.Aplicar_Dto
            };

            PrecioDescuentoProducto precio = new PrecioDescuentoProducto
            {
                precioCalculado = 0, //lo recalcula el gestor de precios
                descuentoCalculado = 0, //lo recalcula el gestor de precios
                producto = producto,
                cliente = cliente,
                contacto = contacto,
                cantidad = cantidad,
                aplicarDescuento = producto.Aplicar_Dto || cliente == Constantes.ClientesEspeciales.EL_EDEN
            };

            GestorPrecios.calcularDescuentoProducto(precio);
            productoDTO.precio = precio.precioCalculado;
            productoDTO.aplicarDescuento = precio.aplicarDescuento;
            productoDTO.descuento = precio.descuentoCalculado;

            return Ok(productoDTO);
        }
        /*
        // PUT: api/Productos/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutProducto(string id, Producto producto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != producto.Empresa)
            {
                return BadRequest();
            }

            db.Entry(producto).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ProductoExists(id))
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

        // POST: api/Productos
        [ResponseType(typeof(Producto))]
        public async Task<IHttpActionResult> PostProducto(Producto producto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.Productos.Add(producto);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (ProductoExists(producto.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = producto.Empresa }, producto);
        }

        // DELETE: api/Productos/5
        [ResponseType(typeof(Producto))]
        public async Task<IHttpActionResult> DeleteProducto(string id)
        {
            Producto producto = await db.Productos.FindAsync(id);
            if (producto == null)
            {
                return NotFound();
            }

            db.Productos.Remove(producto);
            await db.SaveChangesAsync();

            return Ok(producto);
        }
        */

        private ProductoDTO.StockProducto CalcularStockProducto(string producto, string almacen)
        {
            ProductoDTO.StockProducto stockProducto = new ProductoDTO.StockProducto
            {
                Almacen = almacen,
                Stock = db.ExtractosProducto.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Número == producto).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).Sum(),
                PendienteEntregar = db.LinPedidoVtas.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Producto == producto && (e.Estado == Constantes.EstadosLineaVenta.EN_CURSO || e.Estado == Constantes.EstadosLineaVenta.PENDIENTE)).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).Sum(),
                PendienteRecibir = db.LinPedidoCmps.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Producto == producto && (e.Estado == Constantes.EstadosLineaVenta.EN_CURSO || e.Estado == Constantes.EstadosLineaVenta.PENDIENTE) && e.Enviado == true).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).Sum(),
                FechaEstimadaRecepcion = (DateTime)db.LinPedidoCmps.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Producto == producto && ((e.Estado == Constantes.EstadosLineaVenta.EN_CURSO || e.Estado == Constantes.EstadosLineaVenta.PENDIENTE) && e.Enviado == true)).Select(e => e.FechaRecepción).DefaultIfEmpty(DateTime.MaxValue).Min(),
                PendienteReposicion = db.PreExtrProductos.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Producto.Número == producto && e.NºTraspaso != null && e.NºTraspaso > 0).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).Sum()
            };

            return stockProducto;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool ProductoExists(string id)
        {
            return db.Productos.Count(e => e.Empresa == id) > 0;
        }
    }
}