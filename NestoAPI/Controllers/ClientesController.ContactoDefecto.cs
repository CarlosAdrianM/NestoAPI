using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>NestoAPI#879 (corte 3): la dirección de facturación y la de entrega por defecto de un cliente.</summary>
    public class ContactoDefectoClienteDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        /// <summary>La ficha principal activa (ClientePrincipal): la dirección de facturación. Null si no tiene.</summary>
        public string ContactoFacturacion { get; set; }
        /// <summary>El ContactoDefecto de la ficha principal: la dirección de entrega por defecto. Null si no tiene.</summary>
        public string ContactoDefecto { get; set; }
        /// <summary>Solo en el PUT: false si ya era la de entrega por defecto (no se ha escrito nada).</summary>
        public bool Cambiado { get; set; }
    }

    /// <summary>NestoAPI#879 (corte 3): cuerpo del PUT.</summary>
    public class PeticionContactoDefectoClienteDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        /// <summary>El contacto (activo, del mismo cliente) que pasa a ser la dirección de entrega por defecto.</summary>
        public string Contacto { get; set; }
    }

    public partial class ClientesController
    {
        /// <summary>NestoAPI#879: las fichas del cliente y la escritura del ContactoDefecto (sustituible en los tests).</summary>
        internal IRepositorioContactoDefecto RepositorioContactoDefecto { get; set; } = new RepositorioContactoDefecto();

        // GET api/Clientes/ContactoDefecto?empresa=1&cliente=42143
        /// <summary>
        /// NestoAPI#879 (corte 3): qué contacto es la dirección de facturación (la ficha principal) y cuál la de entrega por defecto
        /// (el ContactoDefecto de la principal). 404 si el cliente no existe.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Clientes/ContactoDefecto")]
        [ResponseType(typeof(ContactoDefectoClienteDTO))]
        public async Task<IHttpActionResult> GetContactoDefecto(string empresa, string cliente)
        {
            if (string.IsNullOrWhiteSpace(cliente))
            {
                return BadRequest("Falta el cliente.");
            }
            string empresaLimpia = EmpresaODefecto(empresa);
            List<FichaContactoDefecto> fichas = await RepositorioContactoDefecto.LeerFichas(empresaLimpia, cliente.Trim()).ConfigureAwait(false);
            if (fichas == null || !fichas.Any())
            {
                return NotFound();
            }
            return Ok(new ContactoDefectoClienteDTO
            {
                Empresa = empresaLimpia,
                Cliente = cliente.Trim(),
                ContactoFacturacion = ReglaContactoDefecto.Principal(fichas)?.Contacto?.Trim(),
                ContactoDefecto = ReglaContactoDefecto.ContactoEntrega(fichas)
            });
        }

        // PUT api/Clientes/ContactoDefecto   { "Empresa": "1", "Cliente": "42143", "Contacto": "1" }
        /// <summary>
        /// NestoAPI#879 (corte 3): elige qué contacto es la dirección de entrega por defecto (escribe Clientes.ContactoDefecto en la ficha
        /// principal) y deja rastro en Modificaciones (Tabla <see cref="Infraestructure.Clientes.RepositorioContactoDefecto.TABLA_MODIFICACIONES"/>).
        /// El contacto tiene que existir, ser de ese cliente y estar activo (Estado ≥ 0); 400 con el motivo si no. Mismos permisos que
        /// modificar un cliente (PUT api/Clientes): cualquier usuario autenticado. No cambia la ficha principal (la de facturación).
        /// </summary>
        [HttpPut]
        [Authorize]
        [Route("api/Clientes/ContactoDefecto")]
        [ResponseType(typeof(ContactoDefectoClienteDTO))]
        public async Task<IHttpActionResult> PutContactoDefecto([FromBody] PeticionContactoDefectoClienteDTO peticion)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Cliente))
            {
                return BadRequest("Falta el cliente.");
            }
            string empresa = EmpresaODefecto(peticion.Empresa);
            string cliente = peticion.Cliente.Trim();
            string contacto = peticion.Contacto?.Trim();
            List<FichaContactoDefecto> fichas = await RepositorioContactoDefecto.LeerFichas(empresa, cliente).ConfigureAwait(false) ?? new List<FichaContactoDefecto>();
            if (!fichas.Any())
            {
                return Content(System.Net.HttpStatusCode.NotFound, $"No existe el cliente {cliente}.");
            }
            string motivo = ReglaContactoDefecto.MotivoParaNoCambiar(fichas, cliente, contacto);
            if (motivo != null)
            {
                return BadRequest(motivo);
            }

            var respuesta = new ContactoDefectoClienteDTO
            {
                Empresa = empresa,
                Cliente = cliente,
                ContactoFacturacion = ReglaContactoDefecto.Principal(fichas)?.Contacto?.Trim(),
                ContactoDefecto = contacto
            };
            if (ReglaContactoDefecto.YaEsLaDeEntrega(fichas, contacto))
            {
                return Ok(respuesta);
            }

            string anterior = ReglaContactoDefecto.ContactoEntrega(fichas);
            List<string> principales = ReglaContactoDefecto.PrincipalesActivas(fichas).Select(p => p.Contacto?.Trim()).ToList();
            string usuario = UsuarioAuditoriaHelper.Resolver(User, UsuarioAuditoriaHelper.DESCONOCIDO);
            await RepositorioContactoDefecto.Guardar(empresa, cliente, principales, anterior, contacto, usuario).ConfigureAwait(false);
            respuesta.Cambiado = true;
            return Ok(respuesta);
        }
    }
}
