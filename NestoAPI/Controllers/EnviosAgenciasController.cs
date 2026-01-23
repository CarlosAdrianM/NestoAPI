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
using System.Security.Claims;
using NestoAPI.Infraestructure.Seguridad;

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

        // GET: api/EnviosAgencias/5
        [ResponseType(typeof(List<EnvioAgenciaDTO>))]
        public async Task<IHttpActionResult> GetEnviosAgencia(string empresa, int pedido)
        {
            List<EnvioAgenciaDTO> enviosAgencia = await db.EnviosAgencias.Where(e => e.Empresa == empresa && e.Pedido == pedido).Include("AgenciasTransportes").Select(e => new EnvioAgenciaDTO {
                AgenciaId = e.Agencia,
                AgenciaIdentificador = e.AgenciasTransporte.Identificador,
                AgenciaNombre = e.AgenciasTransporte.Nombre,
                Estado = e.Estado,
                Fecha = e.Fecha,
                CodigoBarras = e.CodigoBarras,
                CodigoPostal = e.CodPostal,
                Cliente = e.Cliente,
                Pedido = (int)e.Pedido
            }).ToListAsync();
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

        /// <summary>
        /// Obtiene el último envío de un cliente con información de seguimiento.
        /// Issue #70 - Para TiendasNuevaVision
        /// </summary>
        /// <param name="empresa">Código de empresa</param>
        /// <param name="cliente">Código de cliente</param>
        /// <returns>Información del último envío con URL de seguimiento</returns>
        [HttpGet]
        [Authorize]
        [Route("api/EnviosAgencias/UltimoEnvioCliente")]
        [ResponseType(typeof(UltimoEnvioClienteDTO))]
        public async Task<IHttpActionResult> GetUltimoEnvioCliente(string empresa, string cliente)
        {
            if (string.IsNullOrWhiteSpace(empresa) || string.IsNullOrWhiteSpace(cliente))
            {
                return BadRequest("Los parámetros empresa y cliente son obligatorios");
            }

            // Validación de seguridad según tipo de usuario
            var identity = User.Identity as ClaimsIdentity;
            var validacion = ValidadorAccesoCliente.ValidarAcceso(identity, cliente);
            if (!validacion.Autorizado)
            {
                return Unauthorized();
            }

            var ultimoEnvio = await db.EnviosAgencias
                .Where(e => e.Empresa == empresa &&
                            e.Cliente == cliente &&
                            e.CodigoBarras != null &&
                            e.Estado >= Constantes.Agencias.ESTADO_EN_CURSO)
                .OrderByDescending(e => e.Fecha)
                .ThenByDescending(e => e.Numero)
                .Include(e => e.AgenciasTransporte)
                .Select(e => new UltimoEnvioClienteDTO
                {
                    Pedido = e.Pedido ?? 0,
                    Fecha = e.Fecha,
                    FechaEntrega = e.FechaEntrega,
                    AgenciaId = e.Agencia,
                    AgenciaNombre = e.AgenciasTransporte.Nombre,
                    AgenciaIdentificador = e.AgenciasTransporte.Identificador,
                    NumeroSeguimiento = e.CodigoBarras,
                    CodigoPostal = e.CodPostal,
                    Cliente = e.Cliente,
                    Estado = e.Estado,
                    Bultos = e.Bultos,
                    Observaciones = e.Observaciones
                })
                .FirstOrDefaultAsync();

            if (ultimoEnvio == null)
            {
                return NotFound();
            }

            return Ok(ultimoEnvio);
        }

    }
}