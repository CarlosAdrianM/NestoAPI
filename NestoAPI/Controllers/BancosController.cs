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
using NestoAPI.Models.ApuntesBanco;

namespace NestoAPI.Controllers
{
    public class BancosController : ApiController
    {
        private NVEntities db = new NVEntities();
        
        // GET: api/Bancos
        public IQueryable<Banco> GetBancos()
        {
            return db.Bancos;
        }
        /*
        // GET: api/Bancos/5
        [ResponseType(typeof(Banco))]
        public async Task<IHttpActionResult> GetBanco(string id)
        {
            Banco banco = await db.Bancos.FindAsync(id);
            if (banco == null)
            {
                return NotFound();
            }

            return Ok(banco);
        }

        // PUT: api/Bancos/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutBanco(string id, Banco banco)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != banco.Empresa)
            {
                return BadRequest();
            }

            db.Entry(banco).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!BancoExists(id))
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
        */

        // POST: api/Bancos/CargarFichero
        [HttpPost]
        [Route("api/Bancos/CargarFichero")]
        [ResponseType(typeof(List<ApunteBancarioDTO>))]
        public async Task<IHttpActionResult> CargarFichero([FromBody]string contenido)
        {
            await Task.Delay(1);
            return Ok(new List<ApunteBancarioDTO>());
        }

        /*
        // POST: api/Bancos
        [ResponseType(typeof(Banco))]
        public async Task<IHttpActionResult> PostBanco(Banco banco)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.Bancos.Add(banco);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (BancoExists(banco.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = banco.Empresa }, banco);
        }

        // DELETE: api/Bancos/5
        [ResponseType(typeof(Banco))]
        public async Task<IHttpActionResult> DeleteBanco(string id)
        {
            Banco banco = await db.Bancos.FindAsync(id);
            if (banco == null)
            {
                return NotFound();
            }

            db.Bancos.Remove(banco);
            await db.SaveChangesAsync();

            return Ok(banco);
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

        private bool BancoExists(string empresa, string id)
        {
            return db.Bancos.Any(e => e.Empresa == empresa && e.Número == id);
        }
    }
}