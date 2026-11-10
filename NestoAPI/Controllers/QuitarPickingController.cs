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
            bool todoElPedido = peticion?.Lineas == null || peticion.Lineas.Count == 0;
            ResultadoQuitarPicking resultado = todoElPedido
                ? await servicio.Quitar(empresa.Trim(), pedido, peticion?.Confirmado == true, User).ConfigureAwait(false)
                : await servicio.Quitar(empresa.Trim(), pedido, peticion.Confirmado, User, peticion.Lineas).ConfigureAwait(false);
            switch (resultado.Estado)
            {
                case EstadoQuitarPicking.Quitado:
                    Avisar(resultado.Respuesta);
                    return Ok(resultado.Respuesta);
                case EstadoQuitarPicking.SinPermiso:
                    return Content(HttpStatusCode.Forbidden, resultado.Respuesta);
                case EstadoQuitarPicking.LineasNoValidas:
                    return Content(HttpStatusCode.BadRequest, resultado.Respuesta);
                default:
                    return Content(HttpStatusCode.Conflict, resultado.Respuesta);
            }
        }

        // GET api/Picking/1/930110/LineasConPicking
        /// <summary>
        /// Novedad 1146: las líneas del pedido con picking (Nº Orden, producto, texto, cantidad, picking) y si se les puede quitar (y
        /// por qué no), con las mismas reglas que QuitarPicking. No toca nada. 403 si no es de Almacén o Dirección.
        /// </summary>
        [HttpGet]
        [Route("api/Picking/{empresa}/{pedido:int}/LineasConPicking")]
        [ResponseType(typeof(System.Collections.Generic.List<LineaConPickingDTO>))]
        public async Task<IHttpActionResult> GetLineasConPicking(string empresa, int pedido)
        {
            if (string.IsNullOrWhiteSpace(empresa) || pedido <= 0)
            {
                return BadRequest("Falta la empresa o el pedido.");
            }
            ResultadoLineasConPicking resultado = await servicio.LeerLineas(empresa.Trim(), pedido, User).ConfigureAwait(false);
            if (resultado.SinPermiso)
            {
                return Content(HttpStatusCode.Forbidden, new MensajeAlmacenDTO { Message = "Para quitar el picking hay que ser de Almacén o de Dirección." });
            }
            return Ok(resultado.Lineas);
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
                    var quitadas = (respuesta.LineasQuitadas ?? new System.Collections.Generic.List<LineaQuitadaPickingDTO>()).Where(l => l.Picking == picking).ToList();
                    var apartar = respuesta.Lecturas.Where(l => l.Picking == picking).ToList();
                    if (respuesta.Parcial && !quitadas.Any(l => l.SeRecoge) && apartar.Count == 0)
                    {
                        // Novedad 1146: solo portes, textos o cuentas: en la PDA no cambia nada que ver; que relea sin el aviso fijo
                        avisosAlmacen.HaCambiado(AvisosAlmacen.GrupoRecogida(CasadorEscaneos.ORIGEN_PICKING, picking), usuario, null, ServicioQuitarPicking.DISPOSITIVO);
                        continue;
                    }
                    var aviso = new PedidoSacadoDelPickingAvisoDTO
                    {
                        NumeroOrigen = picking,
                        Pedido = respuesta.Pedido,
                        Usuario = usuario,
                        Apartar = apartar
                    };
                    aviso.Mensaje = MensajeParaLaPda(aviso, respuesta.Parcial ? quitadas : null);
                    avisosAlmacen.PedidoSacadoDelPicking(AvisosAlmacen.GrupoRecogida(CasadorEscaneos.ORIGEN_PICKING, picking), usuario, aviso);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(ex);
            }
        }

        internal static string MensajeParaLaPda(PedidoSacadoDelPickingAvisoDTO aviso, System.Collections.Generic.List<LineaQuitadaPickingDTO> lineasQuitadas = null)
        {
            string quien = UsuarioAuditoriaHelper.ParaMostrar(aviso.Usuario);
            string por = string.IsNullOrEmpty(quien) ? string.Empty : " (" + quien + ")";
            string inicio;
            if (lineasQuitadas != null && lineasQuitadas.Count > 0)
            {
                // Novedad 1146: solo unas líneas; el resto del pedido sigue en el picking
                string productos = string.Join(", ", lineasQuitadas.Where(l => l.SeRecoge)
                    .Select(l => string.IsNullOrWhiteSpace(l.Nombre) ? l.Producto : $"{l.Nombre} ({l.Producto})").Distinct());
                inicio = $"Del pedido {aviso.Pedido} se ha sacado de este picking {(string.IsNullOrEmpty(productos) ? "alguna línea" : productos)}{por}; " +
                    "el resto del pedido sigue";
            }
            else
            {
                inicio = $"El pedido {aviso.Pedido} se ha sacado de este picking{por}";
            }
            return aviso.Apartar.Count == 0
                ? inicio + "."
                : $"{inicio}: aparte {ServicioQuitarPicking.ListaProductos(aviso.Apartar)} para ese pedido.";
        }
    }
}
