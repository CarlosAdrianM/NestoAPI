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
    public class InventarioCuadresController : ApiController
    {
        private NVEntities db = new NVEntities();

        // Carlos 10/11/15: lo pongo para desactivar el Lazy Loading
        public InventarioCuadresController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }


        // GET: api/InventarioCuadres
        public IQueryable<InventarioCuadre> GetInventarioCuadres()
        {
            return db.InventarioCuadres;
        }

        // GET: api/InventarioCuadres/5
        [ResponseType(typeof(InventarioCuadre))]
        public async Task<IHttpActionResult> GetInventarioCuadre(int id)
        {
            InventarioCuadre inventarioCuadre = await db.InventarioCuadres.FindAsync(id);
            if (inventarioCuadre == null)
            {
                return NotFound();
            }

            return Ok(inventarioCuadre);
        }

        // PUT: api/InventarioCuadres/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutInventarioCuadre(int id, InventarioCuadre inventarioCuadre)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != inventarioCuadre.Numero)
            {
                return BadRequest();
            }

            db.Entry(inventarioCuadre).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!InventarioCuadreExists(id))
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

        // POST: api/InventarioCuadres
        [ResponseType(typeof(InventarioCuadre))]
        public async Task<IHttpActionResult> PostInventarioCuadre(InventarioCuadre inventarioCuadre)
        {
            if (!ModelState.IsValid) 
            {
                return BadRequest(ModelState);
            }

            if ((inventarioCuadre.Producto == "") || (inventarioCuadre.Cantidad <= 0))
            {
                throw new Exception("El producto o la cantidad están sin rellenar");
            }

            // Comprobar si el producto existe
            Producto productoEncontrado = db.Productos.SingleOrDefault(p => p.Empresa == inventarioCuadre.Empresa && p.Número == inventarioCuadre.Producto);
            if (productoEncontrado == null)
            {
                productoEncontrado = db.Productos.FirstOrDefault(p => p.Empresa == inventarioCuadre.Empresa && p.Estado >= 0 && p.CodBarras == inventarioCuadre.Producto);
            }
            if (productoEncontrado == null)
            {
                productoEncontrado = db.Productos.FirstOrDefault(p => p.Empresa == inventarioCuadre.Empresa && p.CodBarras == inventarioCuadre.Producto);
            }
            if (productoEncontrado == null)
            {
                throw new Exception(String.Format("Producto {0} no encontrado", inventarioCuadre.Producto));
            }

            inventarioCuadre.Producto = productoEncontrado.Número;

            db.InventarioCuadres.Add(inventarioCuadre);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (InventarioCuadreExists(inventarioCuadre.Numero))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = inventarioCuadre.Numero }, inventarioCuadre);
        }

        // DELETE: api/InventarioCuadres/5
        [ResponseType(typeof(InventarioCuadre))]
        public async Task<IHttpActionResult> DeleteInventarioCuadre(int id)
        {
            InventarioCuadre inventarioCuadre = await db.InventarioCuadres.FindAsync(id);
            if (inventarioCuadre == null)
            {
                return NotFound();
            }

            db.InventarioCuadres.Remove(inventarioCuadre);
            await db.SaveChangesAsync();

            return Ok(inventarioCuadre);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool InventarioCuadreExists(int id)
        {
            return db.InventarioCuadres.Count(e => e.Numero == id) > 0;
        }
    }
}