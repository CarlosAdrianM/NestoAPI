using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes.Embudo;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public partial class ClientesController
    {
        public const string MENSAJE_EMBUDO_SIN_PERMISO = "Solo Dirección o Informática pueden republicar clientes al embudo de Odoo.";

        /// <summary>Embudo de Odoo: el registro dbo.ClientesEmbudo (sustituible en los tests).</summary>
        internal IRepositorioClientesEmbudo RepositorioEmbudo { get; set; } = new RepositorioClientesEmbudoSql();

        internal static bool PuedeUsarEmbudo(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        // POST api/Clientes/Sync/Republicar   { "Bloque": "NV-2026-11-01", "Ensayo": true }
        /// <summary>
        /// Embudo de Odoo (odoo-custom-addons#45): elige un bloque de clientes del vendedor general (como mucho 50) que no han
        /// entrado nunca en el embudo y los encola en Nesto_sync; el job de siempre los publica con BloqueEmbudo y el responsable de
        /// cada contacto. Con Ensayo (por defecto) no escribe nada: dice cuáles serían. 400 si la petición no vale o falta el
        /// script de la tabla; 403 sin permiso.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Clientes/Sync/Republicar")]
        [ResponseType(typeof(ResultadoRepublicarEmbudo))]
        public async Task<IHttpActionResult> PostRepublicarEmbudo([FromBody] PeticionRepublicarEmbudo peticion)
        {
            if (!PuedeUsarEmbudo(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_EMBUDO_SIN_PERMISO);
            }
            try
            {
                return Ok(await new ServicioEmbudoClientes(RepositorioEmbudo).Republicar(peticion, User?.Identity?.Name).ConfigureAwait(false));
            }
            catch (PeticionEmbudoNoValidaException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // POST api/Clientes/Sync/Republicar/Sembrar   { "Ensayo": true, "Filas": [ { "Cliente": "12345", "Fecha": "2026-10-15", "Origen": "Odoo-Web" } ] }
        /// <summary>
        /// Embudo de Odoo: siembra el registro con clientes que ya han pasado por el embudo por otras puertas (la lista la da Odoo),
        /// para que la republicación no los vuelva a meter. Entran como ya enviados (nunca se publican por esto). Idempotente: los
        /// que ya estaban no cambian; los que no existen en la empresa 1 se devuelven aparte. Con Ensayo (por defecto) no escribe.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Clientes/Sync/Republicar/Sembrar")]
        [ResponseType(typeof(ResultadoSembrarEmbudo))]
        public async Task<IHttpActionResult> PostSembrarEmbudo([FromBody] PeticionSembrarEmbudo peticion)
        {
            if (!PuedeUsarEmbudo(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_EMBUDO_SIN_PERMISO);
            }
            try
            {
                return Ok(await new ServicioEmbudoClientes(RepositorioEmbudo).Sembrar(peticion, User?.Identity?.Name).ConfigureAwait(false));
            }
            catch (PeticionEmbudoNoValidaException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
