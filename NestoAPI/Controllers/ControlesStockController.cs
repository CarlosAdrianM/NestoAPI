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
using NestoAPI.Models.Productos;

namespace NestoAPI.Controllers
{
    public class ControlesStockController : ApiController
    {
        private NVEntities db = new NVEntities();
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
            //controlStockProducto.ConsumoAnual = (int)await db.LinPedidoVtas.Where(l => l.Producto == productoId && l.Fecha_Factura > haceDosAnnos).SumAsync(l => l.Cantidad).ConfigureAwait(false);
            controlStockProducto.ConsumoAnual = (int)await db.LinPedidoVtas
                .Where(l => l.Producto == productoId && l.Fecha_Factura > haceDosAnnos)
                .Select(l => (int?)l.Cantidad) // Proyectamos a un tipo nullable para usar DefaultIfEmpty
                .DefaultIfEmpty(0) // Valor predeterminado en caso de que no haya registros
                .SumAsync()
                .ConfigureAwait(false);
            //DateTime fechaPrimerMovimiento = await db.ExtractosProducto.Where(e => e.Número == productoId && e.Cantidad > 0).OrderBy(e => e.Fecha).Select(e => e.Fecha).FirstOrDefaultAsync().ConfigureAwait(false);
            DateTime? fechaNulablePrimerMovimiento = await db.ExtractosProducto
                .Where(e => e.Número == productoId && e.Cantidad > 0)
                .OrderBy(e => e.Fecha)
                .Select(e => (DateTime?)e.Fecha) // Proyectamos a un tipo DateTime? (nullable)
                .DefaultIfEmpty(null) // Valor predeterminado en caso de que no haya registros
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            DateTime fechaPrimerMovimiento;
            if (fechaNulablePrimerMovimiento == null)
            {
                fechaPrimerMovimiento = DateTime.Today;
            }
            else
            {
                fechaPrimerMovimiento = (DateTime)fechaNulablePrimerMovimiento;
            }
            controlStockProducto.MesesAntiguedad = (decimal)(DateTime.Now - fechaPrimerMovimiento).TotalDays / 30;
            controlStockProducto.StockMinimoActual = controlesStock.SingleOrDefault(e => e.Almacén == Constantes.Almacenes.ALGETE)?.StockMínimo ?? 0;

            foreach (var almacen in Constantes.Sedes.ListaSedes)
            {
                var controlStock = controlesStock.SingleOrDefault(a => a.Almacén == almacen);
                ControlStockAlmacenModel controlStockAlmacen = new ControlStockAlmacenModel
                {
                    Almacen = almacen,
                    DiasReaprovisionamiento = controlStockProducto.DiasReaprovisionamiento,
                    DiasStockSeguridad = controlStockProducto.DiasStockSeguridad
                };
                //controlStockAlmacen.ConsumoAnual = (int)await db.LinPedidoVtas.Where(l => l.Almacén == controlStock.Almacén && l.Producto == productoId && l.Fecha_Factura > haceDosAnnos).SumAsync(l => l.Cantidad).ConfigureAwait(false);
                controlStockAlmacen.ConsumoAnual = (int)await db.LinPedidoVtas
                    .Where(l => l.Almacén == almacen && l.Producto == productoId && l.Fecha_Factura > haceDosAnnos)
                    .Select(l => (int?)l.Cantidad) // Proyectamos a un tipo nullable para usar DefaultIfEmpty
                    .DefaultIfEmpty(0) // Valor predeterminado en caso de que no haya registros
                    .SumAsync()
                    .ConfigureAwait(false);
                DateTime? fechaNulablePrimerMovimientoAlmacen = await db.ExtractosProducto
                .Where(e => e.Almacén == almacen && e.Número == productoId && e.Cantidad > 0)
                .OrderBy(e => e.Fecha)
                .Select(e => (DateTime?)e.Fecha) // Proyectamos a un tipo DateTime? (nullable)
                .DefaultIfEmpty(null) // Valor predeterminado en caso de que no haya registros
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

                DateTime fechaPrimerMovimientoAlmacen;
                if (fechaNulablePrimerMovimientoAlmacen == null)
                {
                    fechaPrimerMovimientoAlmacen = DateTime.Now;
                }
                else
                {
                    fechaPrimerMovimientoAlmacen = (DateTime)fechaNulablePrimerMovimientoAlmacen;
                }
                controlStockAlmacen.MesesAntiguedad = (decimal)(DateTime.Now - fechaPrimerMovimientoAlmacen).TotalDays / 30;
                controlStockAlmacen.StockMaximoActual = controlStock != null ? controlStock.StockMáximo : 0;
                controlStockAlmacen.Estacionalidad = controlStock != null ? controlStock.Estacionalidad : string.Empty;
                controlStockAlmacen.Categoria = controlStock != null ? controlStock.Categoria : string.Empty;
                controlStockAlmacen.Multiplos = controlStock != null ? controlStock.Múltiplos : 1;
                controlStockAlmacen.YaExiste = controlStock != null ? true : false;

                controlStockProducto.ControlesStocksAlmacen.Add(controlStockAlmacen);
            }


            return Ok(controlStockProducto);
        }
    
        // PUT: api/ControlesStock/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutControlStock(ControlStock controlStock)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            /*
            if (id != controlStock.Empresa)
            {
                return BadRequest();
            }
            */
            db.Entry(controlStock).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ControlStockExists(controlStock.Empresa, controlStock.Almacén, controlStock.Número))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
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

            db.ControlesStocks.Add(controlStock);

            try
            {
                await db.SaveChangesAsync();
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

            return CreatedAtRoute("DefaultApi", new { Empresa = controlStock.Empresa, Almacen = controlStock.Almacén, Producto = controlStock.Número }, controlStock);
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