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
using NestoAPI.Infraestructure;
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
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
        }

        public EnviosAgenciasController(NVEntities db)
        {
            this.db = db;
            db.Configuration.LazyLoadingEnabled = false;
        }

        private NVEntities db;

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
                Numero = e.Numero,
                AgenciaId = e.Agencia,
                AgenciaIdentificador = e.AgenciasTransporte.Identificador,
                AgenciaNombre = e.AgenciasTransporte.Nombre,
                Estado = e.Estado,
                Retorno = e.Retorno,
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

            // Issue #135: Si la etiqueta pasa a estado >= en_curso, convertir el sentinel
            // de reembolso a 0 para evitar enviar valores negativos a la agencia
            if (enviosAgencia.Estado >= Constantes.Agencias.ESTADO_EN_CURSO && enviosAgencia.Reembolso < 0)
            {
                enviosAgencia.Reembolso = 0;
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


        // DELETE: api/EnviosAgencias/5
        [Authorize]
        [ResponseType(typeof(EnviosAgencia))]
        public async Task<IHttpActionResult> DeleteEnviosAgencia(int id)
        {
            EnviosAgencia enviosAgencia = await db.EnviosAgencias.FindAsync(id);
            if (enviosAgencia == null)
            {
                return NotFound();
            }

            if (enviosAgencia.Estado >= Constantes.Agencias.ESTADO_EN_CURSO)
            {
                return BadRequest("Solo se pueden eliminar etiquetas pendientes (Estado < 0)");
            }

            db.EnviosAgencias.Remove(enviosAgencia);
            await db.SaveChangesAsync();

            return Ok(enviosAgencia);
        }

        [HttpPost]
        [Authorize]
        [Route("api/EnviosAgencias/CrearEtiquetaPendiente")]
        [ResponseType(typeof(EnvioAgenciaDTO))]
        public async Task<IHttpActionResult> CrearEtiquetaPendiente(CrearEtiquetaPendienteDTO request)
        {
            if (request == null)
            {
                return BadRequest("El request no puede ser nulo");
            }

            var pedido = await db.CabPedidoVtas
                .Include(p => p.LinPedidoVtas)
                .FirstOrDefaultAsync(p => p.Empresa == request.Empresa && p.Número == request.Pedido);

            if (pedido == null)
            {
                return NotFound();
            }

            bool yaExisteEtiquetaPendiente = await db.EnviosAgencias
                .AnyAsync(e => e.Empresa == request.Empresa && e.Pedido == request.Pedido && e.Estado < Constantes.Agencias.ESTADO_EN_CURSO);

            if (yaExisteEtiquetaPendiente)
            {
                return Conflict();
            }

            var direccion = await db.Clientes
                .Include(c => c.PersonasContactoClientes)
                .FirstOrDefaultAsync(c => c.Empresa == pedido.Empresa && c.Nº_Cliente == pedido.Nº_Cliente && c.Contacto == pedido.Contacto);

            if (direccion == null)
            {
                return BadRequest("No se encontró la dirección del contacto del pedido");
            }

            var telefono = new Telefono(direccion.Teléfono);
            var correo = new CorreoCliente(direccion.PersonasContactoClientes);

            decimal reembolso;
            if (!request.CobrarReembolso)
            {
                reembolso = Constantes.Agencias.REEMBOLSO_NO_COBRAR;
            }
            else if (request.ImporteReembolso.HasValue)
            {
                reembolso = request.ImporteReembolso.Value;
            }
            else
            {
                reembolso = 0;
            }

            var envio = new EnviosAgencia
            {
                Empresa = request.Empresa,
                Pedido = request.Pedido,
                Cliente = pedido.Nº_Cliente,
                Contacto = pedido.Contacto,
                Agencia = request.Agencia,
                Estado = (short)Constantes.Agencias.ESTADO_PENDIENTE,
                Retorno = request.Retorno,
                Reembolso = reembolso,
                Fecha = DateTime.Today,
                FechaModificacion = DateTime.Now,
                Bultos = 1,
                Servicio = 0,
                Horario = 0,
                Nombre = direccion.Nombre?.Trim() ?? "",
                Direccion = direccion.Dirección?.Trim() ?? "",
                CodPostal = direccion.CodPostal?.Trim() ?? "",
                Poblacion = direccion.Población?.Trim() ?? "",
                Provincia = direccion.Provincia?.Trim() ?? "",
                Telefono = telefono.FijoUnico(),
                Movil = telefono.MovilUnico(),
                Email = correo.CorreoAgencia(),
                Atencion = direccion.Nombre?.Trim() ?? "",
                Observaciones = pedido.Comentarios,
                Pais = 1
            };

            db.EnviosAgencias.Add(envio);
            await db.SaveChangesAsync();

            var dto = new EnvioAgenciaDTO(envio);
            return CreatedAtRoute("DefaultApi", new { id = envio.Numero }, dto);
        }

        [HttpPut]
        [Authorize]
        [Route("api/EnviosAgencias/ActualizarDireccionEtiqueta/{id}")]
        public async Task<IHttpActionResult> ActualizarDireccionEtiqueta(int id, ActualizarDireccionEtiquetaDTO request)
        {
            if (request == null)
            {
                return BadRequest("El request no puede ser nulo");
            }

            var envio = await db.EnviosAgencias.FindAsync(id);
            if (envio == null)
            {
                return NotFound();
            }

            if (envio.Estado >= Constantes.Agencias.ESTADO_EN_CURSO)
            {
                return BadRequest("Solo se puede actualizar la dirección de etiquetas pendientes");
            }

            var direccion = await db.Clientes
                .Include(c => c.PersonasContactoClientes)
                .FirstOrDefaultAsync(c => c.Empresa == request.Empresa && c.Nº_Cliente == request.Cliente && c.Contacto == request.Contacto);

            if (direccion == null)
            {
                return BadRequest("No se encontró la dirección del contacto");
            }

            var telefono = new Telefono(direccion.Teléfono);
            var correo = new CorreoCliente(direccion.PersonasContactoClientes);

            envio.Contacto = request.Contacto;
            envio.Nombre = direccion.Nombre?.Trim() ?? "";
            envio.Direccion = direccion.Dirección?.Trim() ?? "";
            envio.CodPostal = direccion.CodPostal?.Trim() ?? "";
            envio.Poblacion = direccion.Población?.Trim() ?? "";
            envio.Provincia = direccion.Provincia?.Trim() ?? "";
            envio.Telefono = telefono.FijoUnico();
            envio.Movil = telefono.MovilUnico();
            envio.Email = correo.CorreoAgencia();
            envio.Atencion = direccion.Nombre?.Trim() ?? "";
            envio.FechaModificacion = DateTime.Now;

            await db.SaveChangesAsync();

            return Ok(new EnvioAgenciaDTO(envio));
        }

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