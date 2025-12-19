using NestoAPI.Models;
using NestoAPI.Models.Productos;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class ControlesStockController : ApiController
    {
        private NVEntities db = new NVEntities();

        public ControlesStockController()
        {
        }

        public ControlesStockController(NVEntities context)
        {
            db = context;
        }
        /*
        // GET: api/ControlesStock
        public IQueryable<ControlStock> GetControlesStocks()
        {
            return db.ControlesStocks;
        }
        */

        // GET: api/ControlesStock?productoId=17404
        [ResponseType(typeof(ControlStockProductoModel))]
        public async Task<IHttpActionResult> GetControlStock(string productoId)
        {
            var controlesStock = await db.ControlesStocks.Where(e => e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && e.Número == productoId).ToListAsync().ConfigureAwait(false);
            if (controlesStock == null)
            {
                controlesStock = new List<ControlStock>();
            }
            if (!controlesStock.Any())
            {
                controlesStock.Add(new ControlStock());
            }

            var proveedor = await db.ProveedoresProductoes
                .Where(e => e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && e.Nº_Producto == productoId)
                .OrderBy(p => p.Orden)
                .Select(e => e.Proveedore)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            if (proveedor == null)
            {
                return NotFound();
            }

            ControlStockProductoModel controlStockProducto = new ControlStockProductoModel
            {
                ProductoId = productoId,
                DiasReaprovisionamiento = proveedor.DíasEnServir,
                DiasStockSeguridad = 7
            };
            DateTime haceDosAnnos = DateTime.Today.AddMonths(-24);

            controlStockProducto.ConsumoAnual = (int)await db.LinPedidoVtas
                .Where(l => l.Producto == productoId && l.Fecha_Factura > haceDosAnnos && Constantes.Sedes.ListaSedes.Contains(l.Almacén))
                .Select(l => (int?)l.Cantidad) // Proyectamos a un tipo nullable para usar DefaultIfEmpty
                .DefaultIfEmpty(0) // Valor predeterminado en caso de que no haya registros
                .SumAsync()
                .ConfigureAwait(false);

            DateTime? fechaNulablePrimerMovimiento = await db.ExtractosProducto
                .Where(e => e.Número == productoId && e.Cantidad > 0 && Constantes.Sedes.ListaSedes.Contains(e.Almacén))
                .OrderBy(e => e.Fecha)
                .Select(e => (DateTime?)e.Fecha) // Proyectamos a un tipo DateTime? (nullable)
                .DefaultIfEmpty(null) // Valor predeterminado en caso de que no haya registros
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            DateTime fechaPrimerMovimiento = fechaNulablePrimerMovimiento == null ? DateTime.Today : (DateTime)fechaNulablePrimerMovimiento;
            controlStockProducto.MesesAntiguedad = (decimal)(DateTime.Now - fechaPrimerMovimiento).TotalDays / 30;
            controlStockProducto.StockMinimoActual = controlesStock.SingleOrDefault(e => e.Almacén == Constantes.Almacenes.ALGETE)?.StockMínimo ?? 0;

            foreach (var almacen in Constantes.Sedes.ListaSedes)
            {
                var controlStock = controlesStock.SingleOrDefault(a => a.Almacén == almacen);
                ControlStockAlmacenModel controlStockAlmacen = new ControlStockAlmacenModel
                {
                    Almacen = almacen,
                    DiasReaprovisionamiento = controlStockProducto.DiasReaprovisionamiento,
                    DiasStockSeguridad = controlStockProducto.DiasStockSeguridad,
                    //controlStockAlmacen.ConsumoAnual = (int)await db.LinPedidoVtas.Where(l => l.Almacén == controlStock.Almacén && l.Producto == productoId && l.Fecha_Factura > haceDosAnnos).SumAsync(l => l.Cantidad).ConfigureAwait(false);
                    ConsumoAnual = (int)await db.LinPedidoVtas
                        .Where(l => l.Almacén == almacen && l.Producto == productoId && l.Fecha_Factura > haceDosAnnos)
                        .Select(l => (int?)l.Cantidad) // Proyectamos a un tipo nullable para usar DefaultIfEmpty
                        .DefaultIfEmpty(0) // Valor predeterminado en caso de que no haya registros
                        .SumAsync()
                        .ConfigureAwait(false)
                };
                DateTime? fechaNulablePrimerMovimientoAlmacen = await db.ExtractosProducto
                .Where(e => e.Almacén == almacen && e.Número == productoId && e.Cantidad > 0)
                .OrderBy(e => e.Fecha)
                .Select(e => (DateTime?)e.Fecha) // Proyectamos a un tipo DateTime? (nullable)
                .DefaultIfEmpty(null) // Valor predeterminado en caso de que no haya registros
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

                DateTime fechaPrimerMovimientoAlmacen = fechaNulablePrimerMovimientoAlmacen == null ? DateTime.Now : (DateTime)fechaNulablePrimerMovimientoAlmacen;
                controlStockAlmacen.MesesAntiguedad = (decimal)(DateTime.Now - fechaPrimerMovimientoAlmacen).TotalDays / 30;
                controlStockAlmacen.StockMaximoActual = controlStock != null ? controlStock.StockMáximo : 0;
                controlStockAlmacen.Estacionalidad = controlStock != null ? controlStock.Estacionalidad : string.Empty;
                controlStockAlmacen.Categoria = controlStock != null ? controlStock.Categoria : string.Empty;
                controlStockAlmacen.Multiplos = controlStock != null ? controlStock.Múltiplos : 1;
                controlStockAlmacen.YaExiste = controlStock != null;

                controlStockProducto.ControlesStocksAlmacen.Add(controlStockAlmacen);
            }


            return Ok(controlStockProducto);
        }

        // GET: api/ControlesStock/ProductosProveedor?proveedorId=65&almacen=ALG
        [HttpGet]
        [Route("api/ControlesStock/ProductosProveedor")]
        [ResponseType(typeof(List<ProductoControlStockDTO>))]
        public async Task<IHttpActionResult> GetProductosProveedor(string proveedorId, string almacen)
        {
            if (string.IsNullOrEmpty(proveedorId) || string.IsNullOrEmpty(almacen))
            {
                return BadRequest("Debe especificar proveedorId y almacen");
            }

            // Obtener productos del proveedor con estado 0
            var productosProveedor = await db.ProveedoresProductoes
                .Where(pp => pp.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && pp.Nº_Proveedor == proveedorId)
                .Join(db.Productos.Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Estado == 0),
                    pp => pp.Nº_Producto,
                    p => p.Número,
                    (pp, p) => new { Producto = p, ProveedorProducto = pp })
                .ToListAsync()
                .ConfigureAwait(false);

            if (!productosProveedor.Any())
            {
                return Ok(new List<ProductoControlStockDTO>());
            }

            // Obtener el proveedor para DiasEnServir
            var proveedor = await db.Proveedores
                .FirstOrDefaultAsync(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == proveedorId)
                .ConfigureAwait(false);

            if (proveedor == null)
            {
                return NotFound();
            }

            int diasReaprovisionamiento = proveedor.DíasEnServir;
            int diasStockSeguridad = 7;
            DateTime haceDosAnnos = DateTime.Today.AddMonths(-24);

            var resultado = new List<ProductoControlStockDTO>();

            foreach (var item in productosProveedor)
            {
                string productoId = item.Producto.Número;

                // Obtener control de stock existente para este almacén
                var controlStock = await db.ControlesStocks
                    .FirstOrDefaultAsync(c => c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO &&
                                              c.Número == productoId &&
                                              c.Almacén == almacen)
                    .ConfigureAwait(false);

                // Calcular consumo anual para el almacén
                int consumoAnual = (int)await db.LinPedidoVtas
                    .Where(l => l.Almacén == almacen && l.Producto == productoId && l.Fecha_Factura > haceDosAnnos)
                    .Select(l => (int?)l.Cantidad)
                    .DefaultIfEmpty(0)
                    .SumAsync()
                    .ConfigureAwait(false);

                // Calcular meses de antigüedad
                DateTime? fechaPrimerMovimiento = await db.ExtractosProducto
                    .Where(e => e.Almacén == almacen && e.Número == productoId && e.Cantidad > 0)
                    .OrderBy(e => e.Fecha)
                    .Select(e => (DateTime?)e.Fecha)
                    .FirstOrDefaultAsync()
                    .ConfigureAwait(false);

                decimal mesesAntiguedad = fechaPrimerMovimiento.HasValue
                    ? (decimal)(DateTime.Now - fechaPrimerMovimiento.Value).TotalDays / 30
                    : 1;

                // Ajustar meses de antigüedad a los límites
                if (mesesAntiguedad < 1) mesesAntiguedad = 1;
                if (mesesAntiguedad > 24) mesesAntiguedad = 24;

                // Calcular stocks
                decimal consumoMedioMensual = consumoAnual / mesesAntiguedad;
                decimal consumoMedioDiario = consumoMedioMensual / 30;
                decimal puntoPedido = consumoMedioDiario * (diasStockSeguridad + diasReaprovisionamiento);

                int stockMinimoCalculado = puntoPedido < 1
                    ? (int)Math.Ceiling(puntoPedido)
                    : (int)Math.Round(puntoPedido, 0, MidpointRounding.AwayFromZero);

                int stockMaximoCalculado = (int)Math.Ceiling(consumoMedioDiario * (diasStockSeguridad + diasReaprovisionamiento * 2));

                var dto = new ProductoControlStockDTO
                {
                    ProductoId = productoId.Trim(),
                    Nombre = item.Producto.Nombre?.Trim(),
                    StockMinimoActual = controlStock?.StockMínimo ?? 0,
                    StockMinimoCalculado = stockMinimoCalculado,
                    StockMaximoActual = controlStock?.StockMáximo ?? 0,
                    StockMaximoCalculado = stockMaximoCalculado,
                    YaExiste = controlStock != null,
                    Categoria = controlStock?.Categoria ?? string.Empty,
                    Estacionalidad = controlStock?.Estacionalidad ?? string.Empty,
                    Multiplos = controlStock?.Múltiplos ?? 1
                };

                resultado.Add(dto);
            }

            return Ok(resultado);
        }

        // PUT: api/ControlesStock/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutControlStock(ControlStock controlStock)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Validar que Fecha_Modificación tenga un valor válido para SQL Server datetime (>= 1753)
            if (controlStock.Fecha_Modificación < new DateTime(1753, 1, 1))
            {
                controlStock.Fecha_Modificación = DateTime.Now;
            }

            // Buscar el registro existente
            var existente = await db.ControlesStocks
                .FirstOrDefaultAsync(c => c.Empresa == controlStock.Empresa &&
                                          c.Almacén == controlStock.Almacén &&
                                          c.Número == controlStock.Número)
                .ConfigureAwait(false);

            if (existente == null)
            {
                return NotFound();
            }

            // Actualizar solo los campos específicos
            existente.StockMínimo = controlStock.StockMínimo;
            existente.StockMáximo = controlStock.StockMáximo;
            existente.Categoria = controlStock.Categoria;
            existente.Estacionalidad = controlStock.Estacionalidad;
            existente.Múltiplos = controlStock.Múltiplos;
            existente.Usuario = string.IsNullOrEmpty(controlStock.Usuario) ? User.Identity.Name : controlStock.Usuario;
            existente.Fecha_Modificación = DateTime.Now;

            try
            {
                _ = await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return StatusCode(HttpStatusCode.NoContent);
        }

        // POST: api/ControlesStock
        [ResponseType(typeof(ControlStock))]
        public async Task<IHttpActionResult> PostControlStock(ControlStock controlStock)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Si no viene Usuario, lo cogemos de Identity
            if (string.IsNullOrEmpty(controlStock.Usuario))
            {
                controlStock.Usuario = User.Identity.Name;
            }

            // Establecer fecha de modificación (siempre usamos Now para evitar problemas con datetime de SQL Server)
            controlStock.Fecha_Modificación = DateTime.Now;

            _ = db.ControlesStocks.Add(controlStock);

            try
            {
                _ = await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (ControlStockExists(controlStock.Empresa, controlStock.Almacén, controlStock.Número))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { controlStock.Empresa, Almacen = controlStock.Almacén, Producto = controlStock.Número }, controlStock);
        }
        /*
        // DELETE: api/ControlesStock/5
        [ResponseType(typeof(ControlStock))]
        public async Task<IHttpActionResult> DeleteControlStock(string id)
        {
            ControlStock controlStock = await db.ControlesStocks.FindAsync(id);
            if (controlStock == null)
            {
                return NotFound();
            }

            db.ControlesStocks.Remove(controlStock);
            await db.SaveChangesAsync();

            return Ok(controlStock);
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

        private bool ControlStockExists(string empresa, string almacen, string producto)
        {
            return db.ControlesStocks.Any(e => e.Empresa == empresa && e.Almacén == almacen && e.Número == producto);
        }
    }
}