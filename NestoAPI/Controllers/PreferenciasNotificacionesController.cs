using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#692: qué notificaciones push (que no son de su pedido) recibe el cliente de la app (TiendasNuevaVision), con un
    /// interruptor por tipo, como <see cref="PreferenciasCorreoController"/> (#532). El cliente sale SIEMPRE del claim
    /// <c>cliente</c> del JWT de <c>/api/auth/token</c>: nunca de la URL ni del cuerpo.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Clientes/PreferenciasNotificaciones")]
    public class PreferenciasNotificacionesController : ApiController
    {
        private readonly ServicioPreferenciasNotificaciones servicio;

        // Como en PreferenciasCorreoController: el contenedor usa el constructor sin parámetros; el otro es para los tests.
        public PreferenciasNotificacionesController()
            : this(new ServicioPreferenciasNotificaciones(new RepositorioPreferenciasNotificacionesSql(CrearContexto())))
        {
        }

        public PreferenciasNotificacionesController(ServicioPreferenciasNotificaciones servicio)
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

        // GET api/Clientes/PreferenciasNotificaciones
        [HttpGet]
        [Route("")]
        [ResponseType(typeof(List<PreferenciaNotificacionDTO>))]
        public async Task<IHttpActionResult> GetPreferenciasNotificaciones()
        {
            (string cliente, _) = ClienteDelToken();
            if (cliente == null)
            {
                return Unauthorized();
            }
            return Ok(await servicio.Leer(Constantes.Empresas.EMPRESA_POR_DEFECTO, cliente).ConfigureAwait(false));
        }

        // PUT api/Clientes/PreferenciasNotificaciones/EventosYCursos   { "Activo": false }
        [HttpPut]
        [Route("{tipo}")]
        [ResponseType(typeof(List<PreferenciaNotificacionDTO>))]
        public async Task<IHttpActionResult> PutPreferenciaNotificacion(string tipo, [FromBody] PreferenciaNotificacionRequest peticion)
        {
            (string cliente, string email) = ClienteDelToken();
            if (cliente == null)
            {
                return Unauthorized();
            }
            if (peticion?.Activo == null)
            {
                return BadRequest("Indique si quiere recibir esta notificación («Activo»: true o false).");
            }
            if (TiposNotificacionCliente.Buscar(tipo) == null)
            {
                return NotFound();
            }
            return Ok(await servicio.Cambiar(Constantes.Empresas.EMPRESA_POR_DEFECTO, cliente, tipo, peticion.Activo.Value, email ?? cliente)
                .ConfigureAwait(false));
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
