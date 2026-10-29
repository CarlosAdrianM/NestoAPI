using NestoAPI.Infraestructure;
using NestoAPI.Infrastructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.PedidosVenta.ConsultaReembolso;
using NestoAPI.Infraestructure.PedidosVenta.Retencion;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#734: el almacén retiene un pedido contra reembolso con un enlace de pago pendiente y consulta a Administración, que
    /// decide si sale con o sin reembolso. Controlador propio para no engordar PedidosVentaController.
    /// </summary>
    [Authorize]
    public class PedidosVentaConsultaReembolsoController : ApiController
    {
        /// <summary>Quién puede preguntar (Agencias de Nesto, packing de Ariadna).</summary>
        internal static readonly string[] GruposQuePreguntan =
        {
            Constantes.GruposSeguridad.ALMACEN, Constantes.GruposSeguridad.TIENDAS, Constantes.GruposSeguridad.DIRECCION
        };

        /// <summary>Quién puede contestar.</summary>
        internal static readonly string[] GruposQueResponden =
        {
            Constantes.GruposSeguridad.ADMINISTRACION, Constantes.GruposSeguridad.DIRECCION
        };

        /// <summary>El servicio (sustituible en los tests).</summary>
        internal Func<ServicioConsultasReembolso> CrearServicio { get; set; } = () =>
            new ServicioConsultasReembolso(new RepositorioConsultasReembolso(), new RepositorioPedidosRetenidos(), promesas: new RepositorioPromesaRiesgo());

        // POST api/PedidosVenta/1/929300/ConsultaReembolso   { "Comentario": "..." }   (cuerpo opcional)
        /// <summary>
        /// Retiene el pedido (modo 5, #698) y avisa a Administración por la campana. Idempotente: si ya hay una consulta abierta, la
        /// devuelve (YaExistia = true) sin duplicarla ni volver a avisar. 400 si falta la tabla o no hay enlaces pendientes; 403 sin
        /// permiso; 404 si el pedido no existe.
        /// </summary>
        [HttpPost]
        [Route("api/PedidosVenta/{empresa}/{numero:int}/ConsultaReembolso")]
        [ResponseType(typeof(RespuestaConsultaReembolsoDTO))]
        public async Task<IHttpActionResult> PostConsultaReembolso(string empresa, int numero, [FromBody] PedirConsultaReembolsoRequest peticion)
        {
            if (!PuedePreguntar(User))
            {
                return Prohibido(TextosConsultaReembolso.ERROR_SIN_PERMISO_PREGUNTAR);
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, peticion?.Usuario);
            return Respuesta(await CrearServicio().Consultar(empresa, numero, peticion, usuario).ConfigureAwait(false));
        }

        // POST api/PedidosVenta/1/929300/ConsultaReembolso/Respuesta
        //   { "ConReembolso": true, "AnularEnlaces": false }
        //   { "ConReembolso": false, "FormaPago": "RCB", "PlazosPago": "CONTADO", "Comentario": "Lo pactó Paloma" }
        /// <summary>
        /// Administración contesta: con reembolso (y, si lo pide, anula los enlaces pendientes) o sin reembolso (cambia la forma de
        /// pago y los plazos a lo pactado; nunca tarjeta o prepago si el pedido no está pagado). Libera el pedido y avisa a quien
        /// preguntó. 400 si la respuesta no vale; 403 sin permiso; 404 si el pedido no existe; 409 si no hay consulta abierta.
        /// </summary>
        [HttpPost]
        [Route("api/PedidosVenta/{empresa}/{numero:int}/ConsultaReembolso/Respuesta")]
        [ResponseType(typeof(RespuestaConsultaReembolsoDTO))]
        public async Task<IHttpActionResult> PostRespuesta(string empresa, int numero, [FromBody] ResponderConsultaReembolsoRequest peticion)
        {
            if (!PuedeResponder(User))
            {
                return Prohibido(TextosConsultaReembolso.ERROR_SIN_PERMISO_RESPONDER);
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, peticion?.Usuario);
            return Respuesta(await CrearServicio().Responder(empresa, numero, peticion, usuario).ConfigureAwait(false));
        }

        // GET api/PedidosVenta/1/929300/ConsultaReembolso
        /// <summary>La última consulta del pedido (abierta o contestada) o 204 si nunca se ha consultado.</summary>
        [HttpGet]
        [Route("api/PedidosVenta/{empresa}/{numero:int}/ConsultaReembolso")]
        [ResponseType(typeof(ConsultaReembolsoDTO))]
        public async Task<IHttpActionResult> GetConsultaReembolso(string empresa, int numero)
        {
            if (!PuedePreguntar(User) && !PuedeResponder(User))
            {
                return Prohibido(TextosConsultaReembolso.ERROR_SIN_PERMISO_PREGUNTAR);
            }
            ConsultaReembolsoDTO consulta = await CrearServicio().Leer(empresa, numero).ConfigureAwait(false);
            return consulta == null ? StatusCode(HttpStatusCode.NoContent) : (IHttpActionResult)Ok(consulta);
        }

        // GET api/PedidosVenta/ConsultasReembolso?abiertas=true&empresa=1
        /// <summary>
        /// Las consultas abiertas, de la más antigua a la más nueva, con el cliente y los enlaces pendientes de ahora (la campana y la
        /// ventanita de Administración). Con abiertas=false, detrás van también las contestadas de los últimos 30 días. Lista vacía si
        /// no está lanzado el script.
        /// </summary>
        [HttpGet]
        [Route("api/PedidosVenta/ConsultasReembolso")]
        [ResponseType(typeof(List<ConsultaReembolsoListadoDTO>))]
        public async Task<IHttpActionResult> GetConsultasReembolso(bool abiertas = true, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (!PuedePreguntar(User) && !PuedeResponder(User))
            {
                return Prohibido(TextosConsultaReembolso.ERROR_SIN_PERMISO_RESPONDER);
            }
            return Ok(await CrearServicio().Listado(empresa, abiertas).ConfigureAwait(false));
        }

        internal static bool PuedePreguntar(IPrincipal usuario) => EsEmpleado(usuario) && GruposQuePreguntan.Any(g => usuario.IsInRoleSinDominio(g));

        internal static bool PuedeResponder(IPrincipal usuario) => EsEmpleado(usuario) && GruposQueResponden.Any(g => usuario.IsInRoleSinDominio(g));

        /// <summary>Un cliente de la tienda (JWT con claim "cliente") nunca.</summary>
        private static bool EsEmpleado(IPrincipal usuario)
            => usuario != null && (usuario.Identity as ClaimsIdentity)?.FindFirst("cliente") == null;

        private IHttpActionResult Respuesta(ResultadoConsultaReembolso resultado)
        {
            if (resultado.NoEncontrado)
            {
                return NotFound();
            }
            if (resultado.Error != null)
            {
                return Error(resultado.Conflicto ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, resultado.Error);
            }
            return Ok(resultado.Respuesta);
        }

        private IHttpActionResult Prohibido(string mensaje) => Error(HttpStatusCode.Forbidden, mensaje);

        /// <summary>Todos los errores con el mismo cuerpo que BadRequest(mensaje): { "Message": "..." }.</summary>
        private IHttpActionResult Error(HttpStatusCode codigo, string mensaje)
            => Request != null ? ResponseMessage(Request.CreateErrorResponse(codigo, mensaje)) : (IHttpActionResult)Content(codigo, mensaje);
    }
}
