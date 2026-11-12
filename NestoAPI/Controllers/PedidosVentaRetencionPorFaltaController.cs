using NestoAPI.Infraestructure.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#858 punto 4: el pedido «todo junto» al que le falta algo de pago queda retenido hasta que decida el vendedor.
    /// Controlador propio para no engordar PedidosVentaController.
    /// </summary>
    [Authorize]
    public class PedidosVentaRetencionPorFaltaController : ApiController
    {
        /// <summary>El servicio (sustituible en los tests).</summary>
        internal Func<ServicioRetencionesPorFalta> CrearServicio { get; set; } = () =>
            new ServicioRetencionesPorFalta(new RepositorioRetencionesPorFaltaSql(), RetencionesPorFaltaJobsService.LeerComerciales,
                avisos: new Infraestructure.Notificaciones.AvisosAlmacenSignalR()); // NestoAPI#858: refresca el packing de las PDA

        // GET api/PedidosVenta/RetenidosPorFalta
        /// <summary>
        /// Los pedidos retenidos por falta (Pendiente) que puede decidir quien pregunta: todos para Almacén, Administración, Dirección e
        /// Informática; los suyos (los que hizo o de los que es vendedor) para los demás. Del plazo más cercano al más lejano. 503 sin script.
        /// </summary>
        [HttpGet]
        [Route("api/PedidosVenta/RetenidosPorFalta")]
        [ResponseType(typeof(List<RetencionPorFaltaDTO>))]
        public async Task<IHttpActionResult> GetRetenidosPorFalta()
        {
            return Responder(await CrearServicio().Leer(User).ConfigureAwait(false));
        }

        // POST api/PedidosVenta/1/930500/RetencionPorFalta/Decidir   { "Decision": "ServirLoQueHay" | "Esperar" }
        /// <summary>
        /// «ServirLoQueHay»: el pedido sigue al packing sin lo que falta (queda pendiente como siempre). «Esperar»: el pedido sale del
        /// picking (lo cogido pasa a «pendiente de ubicar») y espera completo; solo con el picking terminado (si no, 409). 400 decisión no
        /// válida; 403 si no es quien hizo el pedido, su vendedor ni de los grupos; 409 si no está retenido o ya se ha decidido; 503 sin script.
        /// </summary>
        [HttpPost]
        [Route("api/PedidosVenta/{empresa}/{pedido:int}/RetencionPorFalta/Decidir")]
        [ResponseType(typeof(RetencionPorFaltaDTO))]
        public async Task<IHttpActionResult> PostDecidir(string empresa, int pedido, [FromBody] DecidirRetencionPorFaltaDTO peticion)
        {
            return Responder(await CrearServicio().Decidir(empresa, pedido, peticion, User).ConfigureAwait(false));
        }

        private IHttpActionResult Responder<T>(ResultadoRetencionPorFalta<T> resultado)
        {
            if (resultado.Codigo == HttpStatusCode.OK)
            {
                return Ok(resultado.Contenido);
            }
            return Content(resultado.Codigo, new { Message = resultado.Mensaje });
        }
    }
}
