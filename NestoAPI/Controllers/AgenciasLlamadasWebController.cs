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
    public class AgenciasLlamadasWebController : ApiController
    {
        private NVEntities db = new NVEntities();

        // GET: api/AgenciasLlamadasWeb
        public IQueryable<AgenciaLlamadaWeb> GetAgenciasLlamadasWeb()
        {
            return db.AgenciasLlamadasWeb;
        }

        // GET: api/AgenciasLlamadasWeb/5
        [ResponseType(typeof(AgenciaLlamadaWeb))]
        public async Task<IHttpActionResult> GetAgenciaLlamadaWeb(int id)
        {
            AgenciaLlamadaWeb agenciaLlamadaWeb = await db.AgenciasLlamadasWeb.FindAsync(id);
            if (agenciaLlamadaWeb == null)
            {
                return NotFound();
            }

            return Ok(agenciaLlamadaWeb);
        }

        // PUT: api/AgenciasLlamadasWeb/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutAgenciaLlamadaWeb(int id, AgenciaLlamadaWeb agenciaLlamadaWeb)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != agenciaLlamadaWeb.Id)
            {
                return BadRequest();
            }

            db.Entry(agenciaLlamadaWeb).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!AgenciaLlamadaWebExists(id))
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

        // POST: api/AgenciasLlamadasWeb
        [ResponseType(typeof(AgenciaLlamadaWeb))]
        public async Task<IHttpActionResult> PostAgenciaLlamadaWeb(AgenciaLlamadaWeb agenciaLlamadaWeb)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.AgenciasLlamadasWeb.Add(agenciaLlamadaWeb);
            try
            {
                await db.SaveChangesAsync();
            } 
            catch (Exception ex)
            {
                throw ex;
            }
            

            return CreatedAtRoute("DefaultApi", new { id = agenciaLlamadaWeb.Id }, agenciaLlamadaWeb);
        }

        // DELETE: api/AgenciasLlamadasWeb/5
        [ResponseType(typeof(AgenciaLlamadaWeb))]
        public async Task<IHttpActionResult> DeleteAgenciaLlamadaWeb(int id)
        {
            AgenciaLlamadaWeb agenciaLlamadaWeb = await db.AgenciasLlamadasWeb.FindAsync(id);
            if (agenciaLlamadaWeb == null)
            {
                return NotFound();
            }

            db.AgenciasLlamadasWeb.Remove(agenciaLlamadaWeb);
            await db.SaveChangesAsync();

            return Ok(agenciaLlamadaWeb);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool AgenciaLlamadaWebExists(int id)
        {
            return db.AgenciasLlamadasWeb.Count(e => e.Id == id) > 0;
        }
    }
}