using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>NestoAPI#789 (épica #784): rellenar el inventario de una tienda desde Nesto.</summary>
    public partial class InventariosController
    {
        /// <summary>Quién puede rellenar el inventario de una tienda.</summary>
        internal static readonly string[] GRUPOS_RELLENAR_INVENTARIO =
        {
            Constantes.GruposSeguridad.TIENDAS,
            Constantes.GruposSeguridad.ALMACEN,
            Constantes.GruposSeguridad.ADMINISTRACION,
            Constantes.GruposSeguridad.DIRECCION,
            Constantes.GruposSeguridad.INFORMATICA
        };

        private IServicioRellenarInventario servicioRellenar;

        /// <summary>Los tests lo sustituyen; si no, el de verdad sobre la conexión de <c>db</c>.</summary>
        internal IServicioRellenarInventario ServicioRellenar
        {
            get
            {
                return servicioRellenar ?? (servicioRellenar = new ServicioRellenarInventario(new RepositorioRellenarInventarioSql(db)));
            }
            set
            {
                servicioRellenar = value;
            }
        }

        // POST: api/Inventarios/Rellenar  { "Empresa": "1", "Almacen": "REI", "Fecha": "2026-11-21" }
        /// <summary>
        /// El INSERT inicial del inventario de la tienda (lo que se hacía a mano desde SSMS): una fila por producto no ficticio con
        /// stock distinto de cero en el almacén (suma de ExtractoProducto de las empresas 1 y 3), StockReal 0, Estado 1,
        /// Aplicacion «NestoAPI» y Usuario el del token (el mismo que luego cuenta y contabiliza). 400 si el almacén tiene
        /// control de ubicaciones (Algete); 409 con Message y Pendientes si ya hay filas pendientes ese día; 403 sin rol.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Inventarios/Rellenar")]
        [ResponseType(typeof(RellenarInventarioRespuestaDTO))]
        public async Task<IHttpActionResult> PostRellenar(RellenarInventarioDTO peticion)
        {
            if (!PuedeRellenarInventario(User))
            {
                return Content(HttpStatusCode.Forbidden, new { Message = "No tiene permiso para rellenar el inventario." });
            }

            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            ResultadoRellenarInventario resultado = await ServicioRellenar.Rellenar(peticion, usuario).ConfigureAwait(false);
            switch (resultado.Estado)
            {
                case EstadoRellenarInventario.Ok:
                    return Ok(resultado.Respuesta);
                case EstadoRellenarInventario.YaRellenado:
                    return Content(HttpStatusCode.Conflict, new { Message = resultado.Mensaje, resultado.Pendientes });
                default:
                    return BadRequest(resultado.Mensaje);
            }
        }

        internal static bool PuedeRellenarInventario(IPrincipal usuario)
        {
            return usuario != null && GRUPOS_RELLENAR_INVENTARIO.Any(g => usuario.IsInRoleSinDominio(g));
        }
    }
}
