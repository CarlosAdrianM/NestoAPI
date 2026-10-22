using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#532 (bajas por tipo): qué correos automáticos recibe el cliente de la app (TiendasNuevaVision) y cómo darse de
    /// baja de uno sin dejar de recibir los demás. El cliente sale SIEMPRE del claim <c>cliente</c> del JWT de
    /// <c>/api/auth/token</c>: nunca de la URL ni del cuerpo, así que nadie puede tocar las preferencias de otro.
    /// Va en un controlador aparte (y no en ClientesController) para no engordar ese, igual que CuponesController.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Clientes/PreferenciasCorreo")]
    public class PreferenciasCorreoController : ApiController
    {
        private readonly ServicioPreferenciasCorreo servicio;

        // Los controllers se resuelven por el contenedor (AddControllersAsServices), que elige el constructor que puede
        // construir entero: el sin parámetros. El otro es para los tests.
        public PreferenciasCorreoController()
            : this(new ServicioPreferenciasCorreo(new RepositorioPreferenciasCorreoSql(CrearContexto())))
        {
        }

        public PreferenciasCorreoController(ServicioPreferenciasCorreo servicio)
        {
            this.servicio = servicio;
        }

        private static NVEntities CrearContexto()
        {
            var db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            return db;
        }

        // GET api/Clientes/PreferenciasCorreo
        [HttpGet]
        [Route("")]
        [ResponseType(typeof(List<PreferenciaCorreoDTO>))]
        public async Task<IHttpActionResult> GetPreferenciasCorreo()
        {
            (string cliente, string email) = ClienteDelToken();
            if (cliente == null)
            {
                return Unauthorized();
            }
            return Ok(await servicio.Leer(Constantes.Empresas.EMPRESA_POR_DEFECTO, cliente, email).ConfigureAwait(false));
        }

        // PUT api/Clientes/PreferenciasCorreo/RecordatorioReposicion   { "Activo": false }
        [HttpPut]
        [Route("{tipo}")]
        [ResponseType(typeof(List<PreferenciaCorreoDTO>))]
        public async Task<IHttpActionResult> PutPreferenciaCorreo(string tipo, [FromBody] PreferenciaCorreoRequest peticion)
        {
            (string cliente, string email) = ClienteDelToken();
            if (cliente == null)
            {
                return Unauthorized();
            }
            if (peticion?.Activo == null)
            {
                return BadRequest("Indique si quiere recibir este correo («Activo»: true o false).");
            }
            if (TiposCorreoCliente.Buscar(tipo) == null)
            {
                return NotFound();
            }
            List<PreferenciaCorreoDTO> preferencias = await servicio.Cambiar(Constantes.Empresas.EMPRESA_POR_DEFECTO, cliente, email, tipo,
                peticion.Activo.Value, email ?? cliente).ConfigureAwait(false);
            return Ok(preferencias);
        }

        /// <summary>El cliente y el correo del JWT de la app. Cliente null si no es un cliente (empleado, vendedor o sin claim).</summary>
        private (string cliente, string email) ClienteDelToken()
        {
            ClaimsIdentity identity = User?.Identity as ClaimsIdentity;
            string cliente = identity?.FindFirst("cliente")?.Value?.Trim();
            string email = identity?.FindFirst(ClaimTypes.Email)?.Value?.Trim();
            return (string.IsNullOrWhiteSpace(cliente) ? null : cliente, string.IsNullOrWhiteSpace(email) ? null : email);
        }
    }
}
