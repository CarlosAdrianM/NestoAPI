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
    public class FormasPagoController : ApiController
    {
        private NVEntities db = new NVEntities();

        // Carlos 06/11/15: lo pongo para desactivar el Lazy Loading
        public FormasPagoController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }


        // GET: api/FormasPago
        [ResponseType(typeof(FormaPagoDTO))]
        public async Task<IHttpActionResult> GetFormasPago(string empresa)
        //public IQueryable<FormaPago> GetFormasPago(string empresa)
        {
            List<FormaPagoDTO> formasPago = await db.FormasPago.Where(l => l.Empresa == empresa && !l.BloquearPagos && l.Número != "TAL").
                Select(f => new FormaPagoDTO
                {
                    formaPago = f.Número.Trim(),
                    descripcion = f.Descripción.Trim(),
                    bloquearPagos = f.BloquearPagos,
                    cccObligatorio = f.CCCObligatorio
                }).ToListAsync();

            return Ok(formasPago);
        }

        // GET: api/FormasPago
        [ResponseType(typeof(FormaPagoDTO))]
        public async Task<IHttpActionResult> GetFormasPago(string empresa, string cliente)
        //public IQueryable<FormaPago> GetFormasPago(string empresa)
        {
            Cliente clienteBuscado = db.Clientes.Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.ClientePrincipal == true).SingleOrDefault();

            // Cargamos todas las formas de pago estándar
            List<FormaPago> formasPago;
            formasPago = await db.FormasPago.Where(l => l.Empresa == empresa && !l.BloquearPagos && l.Número != "TAL").ToListAsync();
            
            // Convertimos las formas de pago en DTO
            List<FormaPagoDTO> formasPagoDTO;
            formasPagoDTO = formasPago.Select(f => new FormaPagoDTO
                {
                    formaPago = f.Número.Trim(),
                    descripcion = f.Descripción.Trim(),
                    bloquearPagos = f.BloquearPagos,
                    cccObligatorio = f.CCCObligatorio
                }).ToList();
            
            // Si el cliente no tiene ningún CCC quitamos las formas de pago que requieran CCC
            CCC ccc = db.CCCs.Where(c => c.Empresa == empresa && c.Cliente == cliente && c.Estado >= 0).FirstOrDefault();
            if (ccc == null)
            {
                formasPagoDTO = formasPagoDTO.Where(f => f.cccObligatorio == false).ToList();
            }

            return Ok(formasPagoDTO);
        }


        /*
        // GET: api/FormasPago/5
        [ResponseType(typeof(FormaPago))]
        public async Task<IHttpActionResult> GetFormaPago(string id)
        {
            FormaPago formaPago = await db.FormasPago.FindAsync(id);
            if (formaPago == null)
            {
                return NotFound();
            }

            return Ok(formaPago);
        }

        
        // PUT: api/FormasPago/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutFormaPago(string id, FormaPago formaPago)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != formaPago.Empresa)
            {
                return BadRequest();
            }

            db.Entry(formaPago).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!FormaPagoExists(id))
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

        // POST: api/FormasPago
        [ResponseType(typeof(FormaPago))]
        public async Task<IHttpActionResult> PostFormaPago(FormaPago formaPago)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.FormasPago.Add(formaPago);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (FormaPagoExists(formaPago.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = formaPago.Empresa }, formaPago);
        }

        // DELETE: api/FormasPago/5
        [ResponseType(typeof(FormaPago))]
        public async Task<IHttpActionResult> DeleteFormaPago(string id)
        {
            FormaPago formaPago = await db.FormasPago.FindAsync(id);
            if (formaPago == null)
            {
                return NotFound();
            }

            db.FormasPago.Remove(formaPago);
            await db.SaveChangesAsync();

            return Ok(formaPago);
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

        private bool FormaPagoExists(string id)
        {
            return db.FormasPago.Count(e => e.Empresa == id) > 0;
        }
    }
}