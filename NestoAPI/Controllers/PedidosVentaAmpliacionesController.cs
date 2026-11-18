using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
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
    /// Caso 931007 (Carlos 18/11/26): lo añadido a un pedido en preparación con «PedirAlmacen» se pregunta a Almacén (urgente, ventana roja
    /// de Nesto). Aquí Almacén lo contesta y lo consulta. Controlador propio para no engordar PedidosVentaController.
    /// </summary>
    [Authorize]
    public class PedidosVentaAmpliacionesController : ApiController
    {
        /// <summary>Quién contesta y consulta: Almacén, Dirección e Informática.</summary>
        internal static readonly string[] GruposQueResponden =
        {
            Constantes.GruposSeguridad.ALMACEN, Constantes.GruposSeguridad.DIRECCION, Constantes.GruposSeguridad.INFORMATICA
        };

        /// <summary>El servicio (sustituible en los tests).</summary>
        internal Func<ServicioAmpliacionPedidoEnPreparacion> CrearServicio { get; set; } = ServicioAmpliacionPedidoEnPreparacion.ParaRespuestas;

        // GET api/PedidosVenta/AmpliacionesEnPreparacion?pendientes=true
        /// <summary>Las solicitudes pendientes (la más antigua primero) o, con pendientes=false, también las contestadas de los últimos 7 días.</summary>
        [HttpGet]
        [Route("api/PedidosVenta/AmpliacionesEnPreparacion")]
        [ResponseType(typeof(List<AmpliacionPedidoDTO>))]
        public async Task<IHttpActionResult> GetAmpliaciones(bool pendientes = true)
        {
            if (!PuedeResponder(User))
            {
                return Error(HttpStatusCode.Forbidden, TextosAmpliacionPedido.ERROR_SIN_PERMISO);
            }
            return Ok(await CrearServicio().Listado(pendientes).ConfigureAwait(false));
        }

        // GET api/PedidosVenta/AmpliacionesEnPreparacion/12
        [HttpGet]
        [Route("api/PedidosVenta/AmpliacionesEnPreparacion/{id:int}")]
        [ResponseType(typeof(AmpliacionPedidoDTO))]
        public async Task<IHttpActionResult> GetAmpliacion(int id)
        {
            if (!PuedeResponder(User))
            {
                return Error(HttpStatusCode.Forbidden, TextosAmpliacionPedido.ERROR_SIN_PERMISO);
            }
            AmpliacionPedidoDTO solicitud = await CrearServicio().Leer(id).ConfigureAwait(false);
            return solicitud == null ? Error(HttpStatusCode.NotFound, TextosAmpliacionPedido.ERROR_NO_ENCONTRADA) : Ok(solicitud);
        }

        // POST api/PedidosVenta/AmpliacionesEnPreparacion/12/Respuesta   { "Entra": true }   { "Entra": false, "Motivo": "Ya se ha ido la agencia" }
        /// <summary>
        /// «Sí, lo metemos» (Entra = true): saca el picking del pedido; si lo añadido no lo coge, cuenta como «No» con su motivo. «No llega»
        /// (Entra = false): lo pasa a un pedido nuevo con sus portes. En los dos casos se cierra la ventana en todos los Nesto y se avisa a
        /// quien lo pidió. 200 { Solicitud, Mensaje }; 403 sin permiso; 404 si no existe; 409 si ya estaba contestada; 503 sin la tabla.
        /// </summary>
        [HttpPost]
        [Route("api/PedidosVenta/AmpliacionesEnPreparacion/{id:int}/Respuesta")]
        [ResponseType(typeof(ResultadoRespuestaAmpliacion))]
        public async Task<IHttpActionResult> PostRespuesta(int id, [FromBody] ResponderAmpliacionRequest peticion)
        {
            if (!PuedeResponder(User))
            {
                return Error(HttpStatusCode.Forbidden, TextosAmpliacionPedido.ERROR_SIN_PERMISO);
            }
            if (peticion == null)
            {
                return Error(HttpStatusCode.BadRequest, "Indique si lo añadido entra en el envío de hoy (Entra).");
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, peticion.Usuario);
            ResultadoRespuestaAmpliacion resultado = await CrearServicio().Responder(id, peticion.Entra, peticion.Motivo, usuario).ConfigureAwait(false);
            if (resultado.NoEncontrada)
            {
                return Error(HttpStatusCode.NotFound, TextosAmpliacionPedido.ERROR_NO_ENCONTRADA);
            }
            if (resultado.Conflicto != null)
            {
                return Error(HttpStatusCode.Conflict, resultado.Conflicto);
            }
            return Ok(resultado);
        }

        internal static bool PuedeResponder(IPrincipal usuario)
            => usuario != null && (usuario.Identity as ClaimsIdentity)?.FindFirst("cliente") == null
                && GruposQueResponden.Any(g => usuario.IsInRoleSinDominio(g));

        /// <summary>Todos los errores con el mismo cuerpo que BadRequest(mensaje): { "Message": "..." }.</summary>
        private IHttpActionResult Error(HttpStatusCode codigo, string mensaje)
            => Request != null ? ResponseMessage(Request.CreateErrorResponse(codigo, mensaje)) : (IHttpActionResult)Content(codigo, mensaje);
    }
}
