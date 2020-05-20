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
using System.Net.Mail;
using System.Text;
using NestoAPI.Infraestructure.Agencias;
using System.Web.Http.Cors;

namespace NestoAPI.Controllers
{
    
    
    public class EnviosAgenciasController : ApiController
    {
        public EnviosAgenciasController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }

        private NVEntities db = new NVEntities();

        /*
        // GET: api/EnviosAgencias
        public IQueryable<EnviosAgencia> GetEnviosAgencias()
        {
            return db.EnviosAgencias;
        }
        */
        // GET: api/EnviosAgencias/5
        [ResponseType(typeof(EnviosAgencia))]
        public async Task<IHttpActionResult> GetEnviosAgencia(int id, string cliente)
        {
            //EnviosAgencia enviosAgencia = await db.EnviosAgencias.FindAsync(id);
            EnviosAgencia enviosAgencia = await (from c in db.EnviosAgencias where c.Numero==id && c.Cliente==cliente select c).FirstOrDefaultAsync();
            //EnviosAgencia enviosAgencia = await db.EnviosAgencias.FindAsync(id);
            if (enviosAgencia == null)
            {
                return NotFound();
            }
            
            return Ok(enviosAgencia);
        }

        // GET: api/EnviosAgencias/5
        [ResponseType(typeof(EnviosAgencia))]
        public async Task<IHttpActionResult> GetEnviosAgencia(int id)
        {
            EnviosAgencia enviosAgencia = await db.EnviosAgencias.FindAsync(id);
            if (enviosAgencia == null)
            {
                return NotFound();
            }

            return Ok(enviosAgencia);
        }


        // PUT: api/EnviosAgencias/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutEnviosAgencia(int id, EnviosAgencia enviosAgencia)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != enviosAgencia.Numero)
            {
                return BadRequest();
            }

            db.Entry(enviosAgencia).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!EnviosAgenciaExists(id))
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
        
        
        // POST: api/EnviosAgencias
        [ResponseType(typeof(EnviosAgencia))]
        public async Task<IHttpActionResult> PostEnviosAgencia(EnviosAgencia enviosAgencia)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.EnviosAgencias.Add(enviosAgencia);
            await db.SaveChangesAsync();

            return CreatedAtRoute("DefaultApi", new { id = enviosAgencia.Numero }, enviosAgencia);
        }
        
        /*
        [HttpPost]
        public void PostEnviosAgencia(EnviosAgencia enviosAgencia) {
            // Command line argument must the the SMTP host.
            SmtpClient client = new SmtpClient();
            client.Port = 25;
            client.Host = "smtp.nuevavision.es";
            client.EnableSsl = false;
            //client.Timeout = 10000;
            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            client.UseDefaultCredentials = false;
            //client.Credentials = new System.Net.NetworkCredential("Boletin", "Madrid2010");
            string asunto = "Observación C/" + enviosAgencia.Cliente.Trim() + ", pedido " + enviosAgencia.Pedido;
            string cuerpo = 
                "Nombre: " + enviosAgencia.Nombre + "\n" +
                "Dirección: " + enviosAgencia.Direccion + "\n" +
                "Reembolso: " + enviosAgencia.Reembolso.ToString("C2") + "\n\n" +
                "Observaciones:\n" +
                enviosAgencia.Observaciones + "\n";

            MailMessage mm = new MailMessage(enviosAgencia.Email, enviosAgencia.Empresa1.Email.Trim(), 
                asunto, 
                cuerpo);
            mm.BodyEncoding = UTF8Encoding.UTF8;
            mm.DeliveryNotificationOptions = DeliveryNotificationOptions.OnFailure;

            client.Send(mm);
        }
        */


        /*
        // DELETE: api/EnviosAgencias/5
        [ResponseType(typeof(EnviosAgencia))]
        public async Task<IHttpActionResult> DeleteEnviosAgencia(int id)
        {
            EnviosAgencia enviosAgencia = await db.EnviosAgencias.FindAsync(id);
            if (enviosAgencia == null)
            {
                return NotFound();
            }

            db.EnviosAgencias.Remove(enviosAgencia);
            await db.SaveChangesAsync();

            return Ok(enviosAgencia);
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

        private bool EnviosAgenciaExists(int id)
        {
            return db.EnviosAgencias.Count(e => e.Numero == id) > 0;
        }

        [HttpPost]
        [EnableCors(origins: "*", headers: "*", methods: "*", SupportsCredentials = true)]
        [Route("api/EnviosAgencias/EnviarCorreoEntregaAgencia")]
        public async Task EnviarCorreoEntregaAgencia(EnviosAgencia envio)
        {
            GestorEnviosAgencia gestor = new GestorEnviosAgencia();

            await gestor.EnviarCorreoEntregaAgencia(envio);            
        }
        
    }
}