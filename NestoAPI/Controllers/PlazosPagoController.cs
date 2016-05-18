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
    public class PlazosPagoController : ApiController
    {
        private NVEntities db = new NVEntities();

        // Carlos 06/11/15: lo pongo para desactivar el Lazy Loading
        public PlazosPagoController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }

        // GET: api/PlazosPago
        //public IQueryable<PlazoPago> GetPlazosPago(string empresa)
        //{
        //    return db.PlazosPago.Where(p => p.Empresa == empresa);
        //}

        [ResponseType(typeof(PlazoPagoDTO))]
        public async Task<IHttpActionResult> GetPlazosPago(string empresa)
        //public IQueryable<FormaPago> GetFormasPago(string empresa)
        {
            List<PlazoPagoDTO> plazosPago = await db.PlazosPago.Where(l => l.Empresa == empresa).
                Select(p => new PlazoPagoDTO
                {
                    plazoPago = p.Número.Trim(),
                    descripcion = p.Descripción.Trim(),
                    numeroPlazos = p.Nº_Plazos,
                    diasPrimerPlazo = p.DíasPrimerPlazo,
                    diasEntrePlazos = p.DíasEntrePlazos,
                    mesesPrimerPlazo = p.MesesPrimerPlazo,
                    mesesEntrePlazos = p.MesesEntrePlazos,
                    descuentoPP = p.DtoProntoPago,
                    financiacion = p.Financiacion
                }).ToListAsync();

            return Ok(plazosPago);
        }


        [ResponseType(typeof(PlazoPagoDTO))]
        public async Task<IHttpActionResult> GetPlazosPago(string empresa, string cliente)
        //public IQueryable<FormaPago> GetFormasPago(string empresa)
        {
            Cliente clienteBuscado = db.Clientes.Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.ClientePrincipal == true).SingleOrDefault();

            List<PlazoPagoDTO> plazosPago = await db.PlazosPago.Where(l => l.Empresa == empresa).
                Select(p => new PlazoPagoDTO
                {
                    plazoPago = p.Número.Trim(),
                    descripcion = p.Descripción.Trim(),
                    numeroPlazos = p.Nº_Plazos,
                    diasPrimerPlazo = p.DíasPrimerPlazo,
                    diasEntrePlazos = p.DíasEntrePlazos,
                    mesesPrimerPlazo = p.MesesPrimerPlazo,
                    mesesEntrePlazos = p.MesesEntrePlazos,
                    descuentoPP = p.DtoProntoPago,
                    financiacion = p.Financiacion
                }).ToListAsync();

            // Si el cliente tiene impagados o facturas vencidas, solo admitimos formas de pago de contado
            int tiempoVencidas = 7; // días que tiene que llevar vencida una factura para que no sacar pedido
            DateTime fechaDesdeVencida = System.DateTime.Today.AddDays(-tiempoVencidas);
            ExtractoCliente impagado = db.ExtractosCliente.Where(e => e.Número == cliente && e.ImportePdte > 0 && e.TipoApunte == "4").FirstOrDefault();
            ExtractoCliente deudaVencida = db.ExtractosCliente.Where(e => e.Número == cliente && e.ImportePdte > 0 && e.FechaVto < fechaDesdeVencida).FirstOrDefault();

            if (impagado != null || deudaVencida != null)
            {
                plazosPago = plazosPago.Where(p => p.diasPrimerPlazo == 0 && p.mesesPrimerPlazo == 0 && p.numeroPlazos == 1).ToList();
            }


            return Ok(plazosPago);
        }

        /*
        // GET: api/PlazosPago/5
        [ResponseType(typeof(PlazoPago))]
        public async Task<IHttpActionResult> GetPlazoPago(string id)
        {
            PlazoPago plazoPago = await db.PlazosPago.FindAsync(id);
            if (plazoPago == null)
            {
                return NotFound();
            }

            return Ok(plazoPago);
        }

        // PUT: api/PlazosPago/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutPlazoPago(string id, PlazoPago plazoPago)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != plazoPago.Empresa)
            {
                return BadRequest();
            }

            db.Entry(plazoPago).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!PlazoPagoExists(id))
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

        // POST: api/PlazosPago
        [ResponseType(typeof(PlazoPago))]
        public async Task<IHttpActionResult> PostPlazoPago(PlazoPago plazoPago)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.PlazosPago.Add(plazoPago);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (PlazoPagoExists(plazoPago.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = plazoPago.Empresa }, plazoPago);
        }

        // DELETE: api/PlazosPago/5
        [ResponseType(typeof(PlazoPago))]
        public async Task<IHttpActionResult> DeletePlazoPago(string id)
        {
            PlazoPago plazoPago = await db.PlazosPago.FindAsync(id);
            if (plazoPago == null)
            {
                return NotFound();
            }

            db.PlazosPago.Remove(plazoPago);
            await db.SaveChangesAsync();

            return Ok(plazoPago);
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

        private bool PlazoPagoExists(string id)
        {
            return db.PlazosPago.Count(e => e.Empresa == id) > 0;
        }
    }
}