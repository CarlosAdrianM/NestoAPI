using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System.Data.Entity;
using System.Net.Mail;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#533: peticiones de un usuario a almacén sobre un pedido que ya no puede tocar él.
    /// Aparte de PedidosVentaController a propósito, para no engordarlo más.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/PedidosVenta")]
    public class PedidosVentaSolicitudesController : ApiController
    {
        private readonly NVEntities db;
        private readonly IServicioCorreoElectronico servicioCorreo;

        public PedidosVentaSolicitudesController() : this(new NVEntities(), new ServicioCorreoElectronico())
        {
        }

        // Para los tests (internal: el contenedor solo ve el constructor público sin parámetros)
        internal PedidosVentaSolicitudesController(NVEntities db, IServicioCorreoElectronico servicioCorreo)
        {
            this.db = db;
            this.servicioCorreo = servicioCorreo;
        }

        // POST api/PedidosVenta/SolicitudCambioModo  { "Empresa": "1", "Pedido": 926879, "ModoDeseado": 1, "Comentario": "..." }
        [HttpPost]
        [Route("SolicitudCambioModo")]
        public async Task<IHttpActionResult> PostSolicitudCambioModo([FromBody] SolicitudCambioModoDTO solicitud)
        {
            if (solicitud == null || solicitud.Pedido <= 0 || string.IsNullOrWhiteSpace(solicitud.Empresa))
            {
                return BadRequest("Falta el pedido");
            }
            if (!Constantes.Pedidos.ModosServicio.EsValido(solicitud.ModoDeseado))
            {
                return BadRequest("El modo de entrega no es válido");
            }
            string empresa = solicitud.Empresa.Trim();
            CabPedidoVta cabecera = await db.CabPedidoVtas
                .FirstOrDefaultAsync(c => c.Empresa == empresa && c.Número == solicitud.Pedido)
                .ConfigureAwait(false);
            if (cabecera == null)
            {
                return NotFound();
            }

            byte modoActual = Constantes.Pedidos.ModosServicio.Efectivo(cabecera.ModoServicio, cabecera.ServirJunto);
            string usuario = User?.Identity?.Name ?? "Un usuario";
            MailMessage correo = CambioModoConPicking.CorreoSolicitud(empresa, solicitud.Pedido, cabecera.Nº_Cliente,
                modoActual, solicitud.ModoDeseado, usuario, solicitud.Comentario);
            string correoUsuario = (User?.Identity as ClaimsIdentity)?.FindFirst(ClaimTypes.Email)?.Value;
            if (!string.IsNullOrWhiteSpace(correoUsuario))
            {
                correo.ReplyToList.Add(new MailAddress(correoUsuario));
            }

            return servicioCorreo.EnviarCorreoSMTP(correo)
                ? (IHttpActionResult)Ok("Se lo hemos pedido a almacén. Si todavía están a tiempo, lo cambiarán ellos.")
                : BadRequest("No se ha podido mandar el correo a almacén. Llámales o escríbeles directamente.");
        }
    }

    public class SolicitudCambioModoDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public byte ModoDeseado { get; set; }
        public string Comentario { get; set; }
    }
}
