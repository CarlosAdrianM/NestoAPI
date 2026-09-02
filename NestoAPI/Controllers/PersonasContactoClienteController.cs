using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#447: las personas de contacto del centro, gestionadas desde la app de clientes
    /// por su TITULAR (una persona con cargo 22, factura electrónica). El titular ve a las demás
    /// personas de su cliente y decide qué ve cada una: todo (22), precios sin descuentos (31) o
    /// ni precios ni descuentos (30). Las reglas están en <see cref="PoliticaPersonasContacto"/>;
    /// el cliente sale SIEMPRE del JWT (ValidadorAccesoCliente), nunca de la petición.
    ///
    /// <para>Es el único sitio de la API que sirve datos de terceros al login de la tienda (#428
    /// dejó el login con solo la persona del propio email): por eso exige ser titular y solo
    /// devuelve a las personas del propio cliente.</para>
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Clientes/PersonasContacto")]
    public class PersonasContactoClienteController : ApiController
    {
        private readonly NVEntities db;

        public PersonasContactoClienteController() : this(new NVEntities())
        {
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
        }

        public PersonasContactoClienteController(NVEntities db)
        {
            this.db = db;
        }

        // GET: api/Clientes/PersonasContacto
        [HttpGet]
        [Route("")]
        [ResponseType(typeof(List<PersonaContactoCentroDTO>))]
        public async Task<IHttpActionResult> GetPersonasDelCentro()
        {
            SesionCliente sesion = SesionDelJwt();
            if (sesion == null)
            {
                return Unauthorized();
            }

            List<PersonaContactoCliente> personas = await PersonasDelCliente(sesion.Cliente).ConfigureAwait(false);
            if (!PoliticaPersonasContacto.EsTitular(personas, sesion.Email))
            {
                return Content(HttpStatusCode.Forbidden, "Solo la persona que ve las facturas puede gestionar a las demás.");
            }

            return Ok(personas
                .OrderBy(p => p.Contacto)
                .ThenBy(p => p.Número)
                .Select(p => Mapear(p, sesion.Email))
                .ToList());
        }

        // PUT: api/Clientes/PersonasContacto/{contacto}/{numero}/Cargo
        [HttpPut]
        [Route("{contacto}/{numero}/Cargo")]
        [ResponseType(typeof(PersonaContactoCentroDTO))]
        public async Task<IHttpActionResult> PutCargo(string contacto, string numero, CambioCargoPersonaContactoRequest peticion)
        {
            SesionCliente sesion = SesionDelJwt();
            if (sesion == null)
            {
                return Unauthorized();
            }
            if (peticion == null)
            {
                return BadRequest("Indique el cargo");
            }

            List<PersonaContactoCliente> personas = await PersonasDelCliente(sesion.Cliente).ConfigureAwait(false);
            if (!PoliticaPersonasContacto.EsTitular(personas, sesion.Email))
            {
                return Content(HttpStatusCode.Forbidden, "Solo la persona que ve las facturas puede gestionar a las demás.");
            }

            PersonaContactoCliente persona = personas.FirstOrDefault(p =>
                string.Equals(p.Contacto?.Trim(), contacto?.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.Número?.Trim(), numero?.Trim(), StringComparison.OrdinalIgnoreCase));

            string motivo = PoliticaPersonasContacto.MotivoParaNoCambiar(personas, persona, peticion.Cargo);
            if (motivo != null)
            {
                return BadRequest(motivo);
            }

            if (persona.Cargo != peticion.Cargo)
            {
                persona.Cargo = peticion.Cargo;
                persona.Usuario = sesion.Email;
                persona.Fecha_Modificación = DateTime.Now;
                await db.SaveChangesAsync().ConfigureAwait(false);
            }

            return Ok(Mapear(persona, sesion.Email));
        }

        private Task<List<PersonaContactoCliente>> PersonasDelCliente(string cliente)
        {
            return db.PersonasContactoClientes
                .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.NºCliente == cliente)
                .ToListAsync();
        }

        private static PersonaContactoCentroDTO Mapear(PersonaContactoCliente p, string emailSesion)
        {
            string correo = p.CorreoElectrónico?.Trim();
            return new PersonaContactoCentroDTO
            {
                Contacto = p.Contacto?.Trim(),
                Numero = p.Número?.Trim(),
                Nombre = p.Nombre?.Trim(),
                CorreoElectronico = correo,
                Cargo = p.Cargo,
                Nivel = PoliticaPersonasContacto.TextoNivel(p.Cargo),
                EsTitular = p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO,
                EsYo = !string.IsNullOrWhiteSpace(correo)
                    && string.Equals(correo, emailSesion?.Trim(), StringComparison.OrdinalIgnoreCase),
                TieneCorreo = !string.IsNullOrWhiteSpace(correo)
            };
        }

        private class SesionCliente
        {
            public string Cliente { get; set; }
            public string Email { get; set; }
        }

        /// <summary>Cliente y correo del JWT, con las reglas de acceso del canal app; null si no es un cliente.</summary>
        private SesionCliente SesionDelJwt()
        {
            ClaimsIdentity identity = User?.Identity as ClaimsIdentity;
            string cliente = identity?.FindFirst("cliente")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(cliente))
            {
                return null;
            }
            ValidadorAccesoCliente.ResultadoValidacion acceso = ValidadorAccesoCliente.ValidarAcceso(identity, cliente);
            if (!acceso.Autorizado)
            {
                return null;
            }
            string email = identity.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrWhiteSpace(email))
            {
                return null;
            }
            return new SesionCliente { Cliente = cliente, Email = email.Trim() };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
