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
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
        }

        // Para los tests: la BD llega de fuera, como en el resto de controllers.
        public EmpresasController(NVEntities db)
        {
            this.db = db;
            db.Configuration.LazyLoadingEnabled = false;
        }

        private readonly NVEntities db;

        // GET: api/Empresas
        // NestoAPI#472: se devuelve el DTO (columnas de la tabla, sin navegaciones de EF), no la
        // entidad. La entidad serializaba "Vendedores":[] y Nesto, que ahí tiene un objeto y no una
        // colección, reventaba al deserializar: Agencias se quedaba sin empresas. Ver EmpresaDTO.
        [ResponseType(typeof(List<EmpresaDTO>))]
        public IHttpActionResult GetEmpresas()
        {
            // ToList() ANTES del Select: DesdeEntidad no es traducible a SQL.
            List<EmpresaDTO> empresas = db.Empresas.ToList()
                .Select(EmpresaDTO.DesdeEntidad)
                .ToList();
            return Ok(empresas);
        }

        // GET: api/Empresas/5
        [ResponseType(typeof(EmpresaDTO))]
        public async Task<IHttpActionResult> GetEmpresa(string id)
        {
            Empresa empresa = await db.Empresas.FindAsync(id);
            if (empresa == null)
            {
                return NotFound();
            }

            return Ok(EmpresaDTO.DesdeEntidad(empresa));
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