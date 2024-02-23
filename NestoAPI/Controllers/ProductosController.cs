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
using static NestoAPI.Models.Constantes;
using NestoAPI.Infraestructure.Kits;
using NestoAPI.Infraestructure.Vendedores;

namespace NestoAPI.Controllers
{
    public class ProductosController : ApiController
    {
        private NVEntities db;
        private readonly IProductoService productoService = new ProductoService();

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
                IServicioVendedores _servicioVendedores = new ServicioVendedores();
                var listaVendedores = (await _servicioVendedores.VendedoresEquipo(empresa, vendedor)).Select(v => v.vendedor);
                lineasVenta = lineasVenta
                    .Where(l => listaVendedores.Contains(l.Cliente.Vendedor) || 
                        (l.Cliente.VendedoresClienteGrupoProductoes
                            .FirstOrDefault() != null && 
                         listaVendedores.Contains(l.Cliente.VendedoresClienteGrupoProductoes
                            .FirstOrDefault().Vendedor))
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
            Producto producto = await db.Productos.Include(p => p.Kits).SingleOrDefaultAsync(p => p.Empresa == empresa && p.Número == id).ConfigureAwait(false);
            if (producto == null)
            {
                return NotFound();
            }

            ProductoDTO productoDTO = new ProductoDTO()
            {
                UrlFoto = fichaCompleta ? await ProductoDTO.RutaImagen(id).ConfigureAwait(false) : null,
                PrecioPublicoFinal = fichaCompleta ? await ProductoDTO.LeerPrecioPublicoFinal(id).ConfigureAwait(false) : 0,
                UrlEnlace = fichaCompleta ? await ProductoDTO.RutaEnlace(id).ConfigureAwait(false) : null,
                Producto = producto.Número?.Trim(),
                Nombre = producto.Nombre?.Trim(),
                Tamanno = producto.Tamaño,
                UnidadMedida = producto.UnidadMedida?.Trim(),
                Familia = producto.Familia1.Descripción?.Trim(),
                PrecioProfesional = (decimal)producto.PVP,
                Estado = (short)producto.Estado,
                Grupo = producto.Grupo,
                Subgrupo = producto.SubGruposProducto.Descripción?.Trim(),
                RoturaStockProveedor = producto.RoturaStockProveedor
            };

            foreach (var kit in producto.Kits)
            {
                productoDTO.ProductosKit.Add(new ProductoKit {
                    ProductoId = kit.NúmeroAsociado.Trim(),
                    Cantidad = kit.Cantidad
                });
            }
            // Lo dejo medio-hardcoded porque no quiero que los vendedores vean otros almacenes
            if (!producto.Ficticio && fichaCompleta)
            {
                productoDTO.Stocks.Add(await productoService.CalcularStockProducto(id, Constantes.Productos.ALMACEN_POR_DEFECTO));
                productoDTO.Stocks.Add(await productoService.CalcularStockProducto(id, Constantes.Productos.ALMACEN_TIENDA));
                productoDTO.Stocks.Add(await productoService.CalcularStockProducto(id, Constantes.Almacenes.ALCOBENDAS));
            }            

            return Ok(productoDTO);
        }

        // GET: api/Productos/5
        public async Task<ICollection<ProductoDTO>> GetProducto(string empresa, string filtroNombre, string filtroFamilia, string filtroSubgrupo, string almacen = "")
        {
            IQueryable<Producto> productos = db.Productos.Where(p => p.Empresa == empresa && p.Estado >= Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO && p.Grupo != Constantes.Productos.GRUPO_MATERIAS_PRIMAS);
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

            var productosDTO = await productos.Select(x=>new ProductoDTO
            {
                Producto = x.Número.Trim(),
                Nombre = x.Nombre.Trim(),
                Familia = x.Familia1.Descripción.Trim(),
                Subgrupo = x.SubGruposProducto.Descripción.Trim(),
                PrecioProfesional = (decimal)x.PVP,
                Estado = (short)x.Estado
            }).ToListAsync();

            if (almacen != string.Empty)
            {
                foreach (var productoDTO in productosDTO)
                {
                    productoDTO.Stocks.Add(await productoService.CalcularStockProducto(productoDTO.Producto, almacen));
                }
            }

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

        // GET: api/Productos/Relacionados/5
        [ResponseType(typeof(List<ProductoDTO>))]
        [Route("api/Productos/Relacionados")]
        public async Task<IHttpActionResult> GetProductosRelacionados(string id)
        {
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            // Obtener el producto base
            Producto productoBase = db.Productos.SingleOrDefault(p => p.Empresa == empresa && p.Número == id);
            if (productoBase == null)
            {
                return NotFound();
            }

            // Obtener productos relacionados que cumplen con los criterios
            var productosRelacionados = db.Productos
                .Where(p => p.Empresa == empresa && p.Grupo == productoBase.Grupo && p.SubGrupo == productoBase.SubGrupo && p.PVP > productoBase.PVP && p.Estado == Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO)
                .OrderBy(p => p.PVP);

            // Obtener los tres productos con menor posición en ClasificacionMasVendidos
            var productosOrdenados = db.ClasificacionMasVendidos
                .Where(cm => productosRelacionados.Any(pr => pr.Número == cm.Producto && pr.Empresa == cm.Empresa))
                .OrderBy(cm => cm.Posicion)
                .Take(3)
                .Select(cm => new
                {
                    Producto = productosRelacionados.FirstOrDefault(pr => pr.Número == cm.Producto && pr.Empresa == cm.Empresa)
                })
                .ToList();

            // Mapear los productos a DTO
            var productosDTO = await Task.WhenAll(productosOrdenados.Select(async p => new ProductoDTO
            {
                UrlFoto = await ProductoDTO.RutaImagen(id).ConfigureAwait(false),
                //UrlEnlace = await ProductoDTO.RutaEnlace(id).ConfigureAwait(false),
                Producto = p.Producto.Número?.Trim(),
                Nombre = p.Producto.Nombre?.Trim(),
                PrecioProfesional = (decimal)p.Producto.PVP,
                Estado = (short)p.Producto.Estado
            })).ConfigureAwait(false);

            return Content(HttpStatusCode.OK, productosDTO.ToList(), Configuration.Formatters.JsonFormatter);
        }


        [HttpPost]
        [ResponseType(typeof(Task<int>))]
        [Route("api/Productos/MontarKit")]
        public async Task<IHttpActionResult> PostMontarKit([FromBody] dynamic data)
        {
            // Verificar si las propiedades necesarias existen en el objeto dynamic
            if (data == null || data.empresa == null || data.almacen == null || data.producto == null || data.cantidad == null || data.usuario == null)
            {
                return BadRequest("Parámetros no válidos");
            }

            // Asignar los valores correctos
            string empresa = data.empresa;
            string almacen = data.almacen;
            string producto = data.producto;
            int cantidad = data.cantidad;
            string usuario = data.usuario;

            IUbicacionService ubicacionService = new UbicacionService();
            GestorKits gestorKits = new GestorKits(productoService, ubicacionService);
            int traspaso = await gestorKits.MontarKit(empresa, almacen, producto, cantidad, usuario);
            
            return Ok(traspaso);
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



        // GET: api/Productos/5
        [ResponseType(typeof(ProductoDTO))]
        public async Task<IHttpActionResult> GetProducto(string codigoBarras)
        {
            if (codigoBarras == null)
            {
                throw new Exception("Código de barras no puede ser nulo");
            }

            codigoBarras = codigoBarras.Trim();
            Producto producto = await db.Productos.FirstOrDefaultAsync(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && (p.CodBarras == codigoBarras || p.Número.Trim() == codigoBarras)).ConfigureAwait(false);
            if (producto == null)
            {
                return NotFound();
            }

            IHttpActionResult actionResult = await GetProducto(producto.Empresa, producto.Número, true);

            return actionResult;
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