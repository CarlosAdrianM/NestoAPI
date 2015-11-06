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
    public class FormasVentaController : ApiController
    {
        private NVEntities db = new NVEntities();

        // Carlos 05/11/15: lo pongo para desactivar el Lazy Loading
        public FormasVentaController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }

        // GET: api/FormasVenta
        public IQueryable<FormaVenta> GetFormasVenta(string empresa)
        {
            return db.FormasVenta.Where(f => f.Empresa == empresa);
        }

        /*
        // GET: api/FormasVenta/5
        [ResponseType(typeof(FormaVenta))]
        public async Task<IHttpActionResult> GetFormaVenta(string id)
        {
            FormaVenta formaVenta = await db.FormasVenta.FindAsync(id);
            if (formaVenta == null)
            {
                return NotFound();
            }

            return Ok(formaVenta);
        }

        // PUT: api/FormasVenta/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutFormaVenta(string id, FormaVenta formaVenta)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != formaVenta.Empresa)
            {
                return BadRequest();
            }

            db.Entry(formaVenta).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!FormaVentaExists(id))
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

        // POST: api/FormasVenta
        [ResponseType(typeof(FormaVenta))]
        public async Task<IHttpActionResult> PostFormaVenta(FormaVenta formaVenta)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.FormasVenta.Add(formaVenta);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (FormaVentaExists(formaVenta.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = formaVenta.Empresa }, formaVenta);
        }

        // DELETE: api/FormasVenta/5
        [ResponseType(typeof(FormaVenta))]
        public async Task<IHttpActionResult> DeleteFormaVenta(string id)
        {
            FormaVenta formaVenta = await db.FormasVenta.FindAsync(id);
            if (formaVenta == null)
            {
                return NotFound();
            }

            db.FormasVenta.Remove(formaVenta);
            await db.SaveChangesAsync();

            return Ok(formaVenta);
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

        private bool FormaVentaExists(string id)
        {
            return db.FormasVenta.Count(e => e.Empresa == id) > 0;
        }
    }
}