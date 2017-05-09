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
    public class EmpresasController : ApiController
    {
        // Carlos 12/04/17: lo pongo para desactivar el Lazy Loading
        public EmpresasController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }
        private NVEntities db = new NVEntities();

        // GET: api/Empresas
        public IQueryable<Empresa> GetEmpresas()
        {
            return db.Empresas;
        }

        // GET: api/Empresas/5
        [ResponseType(typeof(Empresa))]
        public async Task<IHttpActionResult> GetEmpresa(string id)
        {
            Empresa empresa = await db.Empresas.FindAsync(id);
            if (empresa == null)
            {
                return NotFound();
            }

            return Ok(empresa);
        }

        // PUT: api/Empresas/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutEmpresa(string id, Empresa empresa)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != empresa.Número)
            {
                return BadRequest();
            }

            db.Entry(empresa).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!EmpresaExists(id))
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

        // POST: api/Empresas
        [ResponseType(typeof(Empresa))]
        public async Task<IHttpActionResult> PostEmpresa(Empresa empresa)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.Empresas.Add(empresa);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (EmpresaExists(empresa.Número))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = empresa.Número }, empresa);
        }

        // DELETE: api/Empresas/5
        [ResponseType(typeof(Empresa))]
        public async Task<IHttpActionResult> DeleteEmpresa(string id)
        {
            Empresa empresa = await db.Empresas.FindAsync(id);
            if (empresa == null)
            {
                return NotFound();
            }

            db.Empresas.Remove(empresa);
            await db.SaveChangesAsync();

            return Ok(empresa);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool EmpresaExists(string id)
        {
            return db.Empresas.Count(e => e.Número == id) > 0;
        }
    }
}