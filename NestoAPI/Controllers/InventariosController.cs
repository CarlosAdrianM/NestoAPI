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

namespace NestoAPI.Controllers
{
    public class InventariosController : ApiController
    {
        private NVEntities db = new NVEntities();
        
        // Carlos 07/12/15: lo pongo para desactivar el Lazy Loading
        public InventariosController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }

        const int ESTADO_INVENTARIO_SIN_CONTABILIZAR = 1;


        // GET: api/Inventarios
        public IQueryable<Inventario> GetInventarios(string empresa, string almacen, DateTime fecha)
        {
            DateTime fechaMasUno = fecha.AddDays(1);
            return db.Inventarios.Where(i => i.Empresa == empresa && i.Almacén == almacen && i.Fecha >= fecha && i.Fecha < fechaMasUno && i.Estado == ESTADO_INVENTARIO_SIN_CONTABILIZAR).OrderByDescending(i => i.Fecha);
        }

        // GET: api/Inventarios/5
        [ResponseType(typeof(Inventario))]
        public async Task<IHttpActionResult> GetInventario(string empresa, string almacen, DateTime fecha, string producto)
        {
            // Comprobar si el producto existe
            Producto productoEncontrado = buscarProducto(empresa, producto);
            if (productoEncontrado == null)
            {
                throw new Exception(String.Format("Producto {0} no encontrado", producto));
            }

            DateTime fechaMasUno = fecha.AddDays(1);
            Inventario inventario = await db.Inventarios.SingleAsync(i => i.Empresa == empresa && i.Almacén == almacen && i.Fecha >= fecha && i.Fecha < fechaMasUno && i.Número == productoEncontrado.Número && i.Estado == ESTADO_INVENTARIO_SIN_CONTABILIZAR);
            if (inventario == null)
            {
                return NotFound();
            }

            return Ok(inventario);
        }

        // PUT: api/Inventarios/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutInventario(int id, Inventario inventario)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != inventario.NºOrden)
            {
                return BadRequest();
            }

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
        [ResponseType(typeof(Inventario))]
        public async Task<IHttpActionResult> PostInventario(Inventario inventario)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            //Producto producto = await db.Productos.SingleAsync(p => p.Empresa == inventario.Empresa && p.Número == inventario.Número);
            Producto producto = buscarProducto(inventario.Empresa, inventario.Número);

            if (producto == null)
            {
                throw new Exception(String.Format("Producto {0} no encontrado", inventario.Número));
            }

            inventario.Número = producto.Número;
            inventario.Descripción = inventario.Descripción ?? producto.Nombre;
            inventario.Familia = inventario.Familia ?? producto.Familia;
            inventario.Grupo = inventario.Grupo ?? producto.Grupo;
            inventario.Subgrupo = inventario.Subgrupo ?? producto.SubGrupo;
            inventario.Estado = 1; // Sin contabilizar
            inventario.Aplicacion = "NestoAPI";

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

        private Producto buscarProducto(string empresa, string producto)
        {
            Producto productoEncontrado = db.Productos.SingleOrDefault(p => p.Empresa == empresa && p.Número == producto);
            if (productoEncontrado == null)
            {
                productoEncontrado = db.Productos.FirstOrDefault(p => p.Empresa == empresa && p.Estado >= 0 && p.CodBarras == producto);
            }
            if (productoEncontrado == null)
            {
                productoEncontrado = db.Productos.FirstOrDefault(p => p.Empresa == empresa && p.CodBarras == producto);
            }
            if (productoEncontrado == null)
            {
                productoEncontrado = db.Productos.FirstOrDefault(p => p.Empresa == empresa && p.Estado >= 0 && p.CodBarras.Contains(producto));
            }
            if (productoEncontrado == null)
            {
                productoEncontrado = db.Productos.FirstOrDefault(p => p.Empresa == empresa && p.CodBarras.Contains(producto));
            }

            return productoEncontrado;
        }
    }
}