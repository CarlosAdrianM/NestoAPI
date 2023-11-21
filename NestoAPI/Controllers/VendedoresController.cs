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
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;

namespace NestoAPI.Controllers
{
    public class VendedoresController : ApiController
    {
        private NVEntities db = new NVEntities();
        private IServicioVendedores Servicio { get; }

        // Carlos 01/03/17: lo pongo para desactivar el Lazy Loading
        public VendedoresController()
        {
            db.Configuration.LazyLoadingEnabled = false;
            Servicio = new ServicioVendedores();
        }
                

        [ResponseType(typeof(VendedorDTO))]
        public async Task<IHttpActionResult> GetVendedores(string empresa)
        //public IQueryable<FormaPago> GetFormasPago(string empresa)
        {
            List<VendedorDTO> vendedores = await db.Vendedores.Where(l => l.Empresa == empresa && l.Estado >= 0).
                Select(p => new VendedorDTO
                {
                    vendedor = p.Número.Trim(),
                    nombre = p.Descripción.Trim()
                }).OrderBy(l => l.nombre)
                .ToListAsync()
                .ConfigureAwait(false);

            return Ok(vendedores);
        }

        [ResponseType(typeof(List<VendedorDTO>))]
        public async Task<IHttpActionResult> GetVendedores(string empresa, string vendedor)
        {
            return Ok(await Servicio.VendedoresEquipo(empresa, vendedor).ConfigureAwait(false));
        }

        /*
        // GET: api/Vendedores
        public IQueryable<Vendedor> GetVendedores(string empresa)
        {
            return db.Vendedores;
        }

        // GET: api/Vendedores/5
        [ResponseType(typeof(Vendedor))]
        public async Task<IHttpActionResult> GetVendedor(string id)
        {
            Vendedor vendedor = await db.Vendedores.FindAsync(id);
            if (vendedor == null)
            {
                return NotFound();
            }

            return Ok(vendedor);
        }

        // PUT: api/Vendedores/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutVendedor(string id, Vendedor vendedor)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != vendedor.Empresa)
            {
                return BadRequest();
            }

            db.Entry(vendedor).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!VendedorExists(id))
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

        // POST: api/Vendedores
        [ResponseType(typeof(Vendedor))]
        public async Task<IHttpActionResult> PostVendedor(Vendedor vendedor)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.Vendedores.Add(vendedor);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (VendedorExists(vendedor.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = vendedor.Empresa }, vendedor);
        }

        // DELETE: api/Vendedores/5
        [ResponseType(typeof(Vendedor))]
        public async Task<IHttpActionResult> DeleteVendedor(string id)
        {
            Vendedor vendedor = await db.Vendedores.FindAsync(id);
            if (vendedor == null)
            {
                return NotFound();
            }

            db.Vendedores.Remove(vendedor);
            await db.SaveChangesAsync();

            return Ok(vendedor);
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

        private bool VendedorExists(string id)
        {
            return db.Vendedores.Count(e => e.Empresa == id) > 0;
        }
    }
}