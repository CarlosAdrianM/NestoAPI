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
        /// NestoAPI#784: el 409 lleva Codigo («YaRellenado» o «YaContabilizado») y PuedeContarOtraVez; «YaContabilizado» (con
        /// Contabilizadas por usuario y traspaso) si ese día ya hay filas contabilizadas en el almacén: se repite con OtraVez = true.
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
                    return Content(HttpStatusCode.Conflict, new YaRellenadoTiendaInventarioDTO
                    {
                        Message = resultado.Mensaje,
                        Codigo = CodigosConflictoInventario.YA_RELLENADO,
                        Pendientes = resultado.Pendientes ?? new System.Collections.Generic.List<PendientesInventarioUsuario>()
                    });
                case EstadoRellenarInventario.YaContabilizado:
                    // NestoAPI#784: se puede repetir con OtraVez = true («Contar otra vez»).
                    return Content(HttpStatusCode.Conflict, new YaRellenadoTiendaInventarioDTO
                    {
                        Message = resultado.Mensaje,
                        Codigo = CodigosConflictoInventario.YA_CONTABILIZADO,
                        PuedeContarOtraVez = true,
                        Contabilizadas = resultado.Contabilizadas ?? new System.Collections.Generic.List<ContabilizadasInventarioUsuario>()
                    });
                default:
                    return BadRequest(resultado.Mensaje);
            }
        }

        private ServicioDeshacerRellenarInventario servicioDeshacerRellenar;

        /// <summary>Los tests lo sustituyen; si no, el de verdad sobre la conexión de <c>db</c>.</summary>
        internal ServicioDeshacerRellenarInventario ServicioDeshacerRellenar
        {
            get => servicioDeshacerRellenar ?? (servicioDeshacerRellenar = new ServicioDeshacerRellenarInventario(new RepositorioDeshacerRellenarInventarioSql(db)));
            set => servicioDeshacerRellenar = value;
        }

        // DELETE: api/Inventarios/Rellenar?almacen=REI&fecha=2026-11-12&empresa=1
        /// <summary>
        /// Deshace el rellenado de una tienda: borra TODAS las filas pendientes (Estado 1, sin traspaso) de ese almacén ese día, de
        /// cualquier usuario (también lo ya contado). Nunca toca lo contabilizado. Solo Dirección e Informática (403); fecha obligatoria y
        /// almacén sin control de ubicaciones (400). 200 con Almacen, Fecha, FilasBorradas, FilasContadas y UnidadesContadas (ceros si
        /// no había nada). Rastro: una fila resumen en Modificaciones («InventarioRellenadoDeshecho»).
        /// </summary>
        [HttpDelete]
        [Authorize]
        [Route("api/Inventarios/Rellenar")]
        [ResponseType(typeof(DeshacerRellenarInventarioRespuestaDTO))]
        public async Task<IHttpActionResult> DeleteRellenar(string almacen = null, System.DateTime? fecha = null, string empresa = null)
        {
            ResultadoDeshacerRellenarInventario resultado = await ServicioDeshacerRellenar.Deshacer(empresa, almacen, fecha,
                ServicioDeshacerRellenarInventario.PuedeDeshacer(User), UsuarioAuditoriaHelper.Resolver(User, null)).ConfigureAwait(false);
            switch (resultado.Estado)
            {
                case EstadoDeshacerRellenarInventario.Ok:
                    return Ok(resultado.Respuesta);
                case EstadoDeshacerRellenarInventario.SinPermiso:
                    return Content(HttpStatusCode.Forbidden, new HttpError(resultado.Mensaje));
                default:
                    return Content(HttpStatusCode.BadRequest, new HttpError(resultado.Mensaje));
            }
        }

        internal static bool PuedeRellenarInventario(IPrincipal usuario)
        {
            return usuario != null && GRUPOS_RELLENAR_INVENTARIO.Any(g => usuario.IsInRoleSinDominio(g));
        }
    }
}
