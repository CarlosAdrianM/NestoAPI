using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infrastructure;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#590 (slice «Quitar picking», 09/11/26): quitar el picking de un pedido desde Nesto, en vez de Nesto viejo
    /// (prdDeshacerUbicacionPicking a pelo, sin rastro y sin avisar a la PDA que lo recoge).
    /// </summary>
    [Authorize]
    public class QuitarPickingController : ApiController
    {
        private readonly IServicioQuitarPicking servicio;
        private readonly IAvisosAlmacen avisosAlmacen;

        public QuitarPickingController(IServicioQuitarPicking servicio, IAvisosAlmacen avisosAlmacen = null)
        {
            this.servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));
            this.avisosAlmacen = avisosAlmacen;
        }

        // POST api/Picking/1/930110/QuitarPicking   { "Confirmado": false }
        /// <summary>
        /// Quita el picking de todas las líneas pendientes del pedido (soltando sus reservas de Ubicaciones) y lo apunta en
        /// Modificaciones. 200 con <see cref="ResultadoQuitarPickingDTO"/>. 409 con Lecturas si el mozo ya ha leído productos de
        /// este pedido en el picking y no viene Confirmado (no se toca nada); con Confirmado, esas lecturas se anulan y la PDA
        /// recibe «pedidoSacadoDelPicking» con lo que tiene que apartar. 409 sin Lecturas si no se puede (albarán, packing,
        /// picking terminado o sin picking). 403 si no es de Almacén o Dirección.
        /// </summary>
        [HttpPost]
        [Route("api/Picking/{empresa}/{pedido:int}/QuitarPicking")]
        [ResponseType(typeof(ResultadoQuitarPickingDTO))]
        public async Task<IHttpActionResult> PostQuitarPicking(string empresa, int pedido, [FromBody] QuitarPickingDTO peticion)
        {
            if (string.IsNullOrWhiteSpace(empresa) || pedido <= 0)
            {
                return BadRequest("Falta la empresa o el pedido.");
            }
            ResultadoQuitarPicking resultado = await servicio.Quitar(empresa.Trim(), pedido, peticion?.Confirmado == true, User).ConfigureAwait(false);
            switch (resultado.Estado)
            {
                case EstadoQuitarPicking.Quitado:
                    Avisar(resultado.Respuesta);
                    return Ok(resultado.Respuesta);
                case EstadoQuitarPicking.SinPermiso:
                    return Content(HttpStatusCode.Forbidden, resultado.Respuesta);
                default:
                    return Content(HttpStatusCode.Conflict, resultado.Respuesta);
            }
        }

        /// <summary>A las PDA de cada picking del que ha salido el pedido: qué apartar y que relean. Nunca rompe la petición.</summary>
        private void Avisar(ResultadoQuitarPickingDTO respuesta)
        {
            if (avisosAlmacen == null || respuesta == null)
            {
                return;
            }
            try
            {
                string usuario = UsuarioAuditoriaHelper.ParaAuditoria(User?.Identity?.Name);
                foreach (int picking in respuesta.Pickings)
                {
                    var aviso = new PedidoSacadoDelPickingAvisoDTO
                    {
                        NumeroOrigen = picking,
                        Pedido = respuesta.Pedido,
                        Usuario = usuario,
                        Apartar = respuesta.Lecturas.Where(l => l.Picking == picking).ToList()
                    };
                    aviso.Mensaje = MensajeParaLaPda(aviso);
                    avisosAlmacen.PedidoSacadoDelPicking(AvisosAlmacen.GrupoRecogida(CasadorEscaneos.ORIGEN_PICKING, picking), usuario, aviso);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(ex);
            }
        }

        internal static string MensajeParaLaPda(PedidoSacadoDelPickingAvisoDTO aviso)
        {
            string quien = UsuarioAuditoriaHelper.ParaMostrar(aviso.Usuario);
            string inicio = $"El pedido {aviso.Pedido} se ha sacado de este picking{(string.IsNullOrEmpty(quien) ? string.Empty : " (" + quien + ")")}";
            return aviso.Apartar.Count == 0
                ? inicio + "."
                : $"{inicio}: aparte {ServicioQuitarPicking.ListaProductos(aviso.Apartar)} para ese pedido.";
        }
    }
}
