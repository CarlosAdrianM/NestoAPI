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
    public class ProveedoresController : ApiController
    {
        private NVEntities db = new NVEntities();

        public ProveedoresController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }

        // GET: api/Proveedores
        public IQueryable<Proveedor> GetProveedores()
        {
            return db.Proveedores;
        }

        // GET: api/Proveedores/5
        [ResponseType(typeof(Proveedor))]
        public async Task<IHttpActionResult> GetProveedor(string id)
        {
            Proveedor proveedor = await db.Proveedores.FindAsync(id);
            if (proveedor == null)
            {
                return NotFound();
            }

            return Ok(proveedor);
        }


        // GET: api/Proveedores/5
        [ResponseType(typeof(Proveedor))]
        public async Task<IHttpActionResult> GetProveedor(string empresa, string filtro)
        {
            if (string.IsNullOrEmpty(filtro))
            {
                return BadRequest();
            }
            List<Proveedor> proveedoresEncontrados = await db.Proveedores
                .Where(p => p.Empresa == empresa && (
                    p.Número == filtro ||
                    p.Nombre.Contains(filtro) ||
                    p.CIF_NIF.Contains(filtro) ||
                    p.Teléfono.Contains(filtro)
                )).ToListAsync();
            

            if (proveedoresEncontrados == null)
            {
                return NotFound();
            }

            return Ok(proveedoresEncontrados);
        }

        // GET: api/Proveedores/5
        [ResponseType(typeof(Proveedor))]
        public async Task<IHttpActionResult> GetProveedor(string empresa, string proveedor, string contacto)
        {
            Proveedor proveedorEncontrado;
            if (!string.IsNullOrEmpty(contacto))
            {
                proveedorEncontrado = await db.Proveedores
                .SingleOrDefaultAsync(p => p.Empresa == empresa && p.Número == proveedor && p.Contacto == contacto);
            }
            else
            {
                proveedorEncontrado = await db.Proveedores
                .SingleOrDefaultAsync(p => p.Empresa == empresa && p.Número == proveedor && p.ProveedorPrincipal);
            }
            
            if (proveedorEncontrado == null)
            {
                return NotFound();
            }

            return Ok(proveedorEncontrado);
        }

        // PUT: api/Proveedores/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutProveedor(string id, Proveedor proveedor)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != proveedor.Empresa)
            {
                return BadRequest();
            }

            db.Entry(proveedor).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ProveedorExists(id))
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

        // POST: api/Proveedores
        [ResponseType(typeof(Proveedor))]
        public async Task<IHttpActionResult> PostProveedor(Proveedor proveedor)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.Proveedores.Add(proveedor);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (ProveedorExists(proveedor.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = proveedor.Empresa }, proveedor);
        }

        // DELETE: api/Proveedores/5
        [ResponseType(typeof(Proveedor))]
        public async Task<IHttpActionResult> DeleteProveedor(string id)
        {
            Proveedor proveedor = await db.Proveedores.FindAsync(id);
            if (proveedor == null)
            {
                return NotFound();
            }

            db.Proveedores.Remove(proveedor);
            await db.SaveChangesAsync();

            return Ok(proveedor);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool ProveedorExists(string id)
        {
            return db.Proveedores.Count(e => e.Empresa == id) > 0;
        }
    }
}