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
    public class SeguimientosClientesController : ApiController
    {
        private NVEntities db = new NVEntities();

        // GET: api/SeguimientosClientes
        public IQueryable<SeguimientoCliente> GetSeguimientosClientes()
        {
            return db.SeguimientosClientes;
        }

        // GET: api/SeguimientosClientes/5
        [ResponseType(typeof(SeguimientoCliente))]
        public async Task<IHttpActionResult> GetSeguimientoCliente(string id)
        {
            SeguimientoCliente seguimientoCliente = await db.SeguimientosClientes.FindAsync(id);
            if (seguimientoCliente == null)
            {
                return NotFound();
            }

            return Ok(seguimientoCliente);
        }

        // PUT: api/SeguimientosClientes/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutSeguimientoCliente(string id, SeguimientoCliente seguimientoCliente)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != seguimientoCliente.Empresa)
            {
                return BadRequest();
            }

            db.Entry(seguimientoCliente).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SeguimientoClienteExists(id))
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

        // POST: api/SeguimientosClientes
        [ResponseType(typeof(SeguimientoCliente))]
        public async Task<IHttpActionResult> PostSeguimientoCliente(SeguimientoCliente seguimientoCliente)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.SeguimientosClientes.Add(seguimientoCliente);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (SeguimientoClienteExists(seguimientoCliente.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = seguimientoCliente.Empresa }, seguimientoCliente);
        }

        // DELETE: api/SeguimientosClientes/5
        [ResponseType(typeof(SeguimientoCliente))]
        public async Task<IHttpActionResult> DeleteSeguimientoCliente(string id)
        {
            SeguimientoCliente seguimientoCliente = await db.SeguimientosClientes.FindAsync(id);
            if (seguimientoCliente == null)
            {
                return NotFound();
            }

            db.SeguimientosClientes.Remove(seguimientoCliente);
            await db.SaveChangesAsync();

            return Ok(seguimientoCliente);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool SeguimientoClienteExists(string id)
        {
            return db.SeguimientosClientes.Count(e => e.Empresa == id) > 0;
        }
    }
}