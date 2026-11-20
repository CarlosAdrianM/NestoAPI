using NestoAPI.Infraestructure.PedidosVenta.Autorizaciones;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Novedades 1258 (Carlos 20/11/26): pedir autorización de un pedido que no pasa la validación de ofertas y descuentos, y
    /// autorizarlo o denegarlo. Puede autorizar quien puede crearlo «de todos modos» (<see cref="ReglaOmitirValidacionPedido"/>); al
    /// autorizarlo se crea por el MISMO POST api/PedidosVenta, a nombre de quien lo pidió. Controlador propio para no engordar
    /// PedidosVentaController. Sin la tabla (Scripts/PedidosAutorizacionesSolicitadas.sql), los seis endpoints dan 503.
    /// </summary>
    [Authorize]
    public class PedidosVentaAutorizacionesController : ApiController
    {
        /// <summary>El servicio (sustituible en los tests).</summary>
        internal Func<ServicioAutorizacionesPedido> CrearServicio { get; set; } = ServicioAutorizacionesPedido.DeProduccion;

        /// <summary>
        /// El alta del pedido: el POST de PedidosVenta con el principal de esta petición (quien autoriza) y sus parámetros para «crearlo
        /// de todos modos». Sustituible en los tests.
        /// </summary>
        internal Func<PedidoVentaDTO, string, Task<IHttpActionResult>> CrearPedido { get; set; }

        private ServicioAutorizacionesPedido servicio;
        private ServicioAutorizacionesPedido Servicio => servicio ?? (servicio = CrearServicio());

        // POST api/PedidosVenta/Autorizaciones   { "Pedido": { …el mismo cuerpo que POST api/PedidosVenta… }, "Comentario": "…" }
        [HttpPost]
        [Route("api/PedidosVenta/Autorizaciones")]
        [ResponseType(typeof(SolicitudAutorizacionCreadaDTO))]
        public async Task<IHttpActionResult> PostSolicitud([FromBody] SolicitarAutorizacionPedidoRequest peticion)
        {
            if (!Servicio.TablaExiste())
            {
                return SinTabla();
            }
            return Traducir(await Servicio.Solicitar(User, peticion).ConfigureAwait(true));
        }

        // GET api/PedidosVenta/Autorizaciones?pendientes=true&mias=false
        [HttpGet]
        [Route("api/PedidosVenta/Autorizaciones")]
        [ResponseType(typeof(List<AutorizacionPedidoDTO>))]
        public async Task<IHttpActionResult> GetSolicitudes(bool pendientes = true, bool mias = false)
        {
            if (!Servicio.TablaExiste())
            {
                return SinTabla();
            }
            return Ok(await Servicio.Listado(User, pendientes, mias).ConfigureAwait(true));
        }

        // GET api/PedidosVenta/Autorizaciones/12
        [HttpGet]
        [Route("api/PedidosVenta/Autorizaciones/{id:int}")]
        [ResponseType(typeof(AutorizacionPedidoDTO))]
        public async Task<IHttpActionResult> GetSolicitud(int id)
        {
            if (!Servicio.TablaExiste())
            {
                return SinTabla();
            }
            return Traducir(await Servicio.Leer(User, id).ConfigureAwait(true));
        }

        // POST api/PedidosVenta/Autorizaciones/12/Autorizar   { "Comentario": "…" }
        [HttpPost]
        [Route("api/PedidosVenta/Autorizaciones/{id:int}/Autorizar")]
        [ResponseType(typeof(ResolucionAutorizacionPedidoDTO))]
        public async Task<IHttpActionResult> PostAutorizar(int id, [FromBody] AutorizarPedidoRequest peticion)
        {
            if (!Servicio.TablaExiste())
            {
                return SinTabla();
            }
            ResultadoAutorizacionPedido resultado = await Servicio.Autorizar(User, id, peticion, CrearPedido ?? CrearPedidoPorElPostDeSiempre)
                .ConfigureAwait(true);
            return resultado.RespuestaAlta ?? Traducir(resultado);
        }

        // POST api/PedidosVenta/Autorizaciones/12/Denegar   { "Motivo": "…" }
        [HttpPost]
        [Route("api/PedidosVenta/Autorizaciones/{id:int}/Denegar")]
        [ResponseType(typeof(ResolucionAutorizacionPedidoDTO))]
        public async Task<IHttpActionResult> PostDenegar(int id, [FromBody] DenegarPedidoRequest peticion)
        {
            if (!Servicio.TablaExiste())
            {
                return SinTabla();
            }
            return Traducir(await Servicio.Denegar(User, id, peticion).ConfigureAwait(true));
        }

        // DELETE api/PedidosVenta/Autorizaciones/12
        [HttpDelete]
        [Route("api/PedidosVenta/Autorizaciones/{id:int}")]
        [ResponseType(typeof(ResolucionAutorizacionPedidoDTO))]
        public async Task<IHttpActionResult> DeleteSolicitud(int id)
        {
            if (!Servicio.TablaExiste())
            {
                return SinTabla();
            }
            return Traducir(await Servicio.Anular(User, id).ConfigureAwait(true));
        }

        /// <summary>El POST de PedidosVenta con esta petición (principal = quien autoriza), como PedidosClienteController.CrearControllerPedidos.</summary>
        private async Task<IHttpActionResult> CrearPedidoPorElPostDeSiempre(PedidoVentaDTO pedido, string autorizador)
        {
            using (var controller = new PedidosVentaController
            {
                RequestContext = RequestContext,
                UsuarioPermisosValidacion = autorizador
            })
            {
                if (Request != null)
                {
                    controller.Request = Request;
                }
                if (Configuration != null)
                {
                    controller.Configuration = Configuration;
                }
                return await controller.PostPedidoVenta(pedido).ConfigureAwait(true);
            }
        }

        private IHttpActionResult SinTabla() => Error(HttpStatusCode.ServiceUnavailable, TextosAutorizacionPedido.ERROR_SIN_TABLA);

        private IHttpActionResult Traducir(ResultadoAutorizacionPedido r)
        {
            if (r.IdExistente.HasValue)
            {
                return Content(HttpStatusCode.Conflict, new
                {
                    Message = r.Mensaje,
                    Codigo = TextosAutorizacionPedido.CODIGO_YA_SOLICITADA,
                    Id = r.IdExistente.Value
                });
            }
            if (r.Mensaje != null)
            {
                return Error(r.Codigo, r.Mensaje);
            }
            return Content(r.Codigo, r.Cuerpo);
        }

        /// <summary>Todos los errores con el mismo cuerpo que BadRequest(mensaje): { "Message": "..." }.</summary>
        private IHttpActionResult Error(HttpStatusCode codigo, string mensaje)
            => Request != null ? ResponseMessage(Request.CreateErrorResponse(codigo, mensaje)) : (IHttpActionResult)Content(codigo, mensaje);
    }
}
