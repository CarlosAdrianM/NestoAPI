using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models.EscanerRemoto;
using System.Net;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#768 (Carlos 30/10/26): «escáner remoto». Ariadna (en el móvil o en la PDA) manda lo que lee y la API se lo pasa
    /// por SignalR (AvisosHub, la conexión de la campana de Nesto) SOLO al Nesto del mismo usuario (sin dominio y sin distinguir
    /// mayúsculas) que tuvo el foco más recientemente. Nesto lo escribe en el campo con el foco si acepta códigos de barras (Nesto#589).
    /// El usuario sale siempre del token, nunca de la petición: no se puede mandar a otro. Gemelas: Nesto#589, Ariadna#69.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/EscanerRemoto")]
    public class EscanerRemotoController : ApiController
    {
        public const string MENSAJE_CLIENTE = "El escáner remoto es solo para los usuarios de Nueva Visión.";

        private readonly IEscanerRemoto servicio;

        public EscanerRemotoController(IEscanerRemoto servicio)
        {
            this.servicio = servicio;
        }

        // GET api/EscanerRemoto/Destino
        /// <summary>A qué Nesto iría lo leído (sin mandar nada): para enseñarlo al abrir la pantalla de Ariadna.</summary>
        [HttpGet]
        [Route("Destino")]
        [ResponseType(typeof(DestinoEscanerRemotoDTO))]
        public IHttpActionResult GetDestino()
        {
            IHttpActionResult rechazo = Rechazo(out string usuario);
            return rechazo ?? Ok(servicio.Destino(usuario));
        }

        // POST api/EscanerRemoto/Lecturas { Codigo, ClaveIdempotencia, FechaHora }
        /// <summary>Manda lo leído al Nesto del mismo usuario. Idempotente por ClaveIdempotencia.</summary>
        [HttpPost]
        [Route("Lecturas")]
        [ResponseType(typeof(DestinoEscanerRemotoDTO))]
        public IHttpActionResult PostLectura([FromBody] LecturaEscanerRemotoDTO lectura)
        {
            IHttpActionResult rechazo = Rechazo(out string usuario);
            if (rechazo != null)
            {
                return rechazo;
            }
            string codigo = lectura?.Codigo?.Trim();
            if (string.IsNullOrEmpty(codigo))
            {
                return BadRequest("Falta el código leído.");
            }
            if (codigo.Length > EscanerRemotoLimites.LONGITUD_MAXIMA_CODIGO)
            {
                return BadRequest($"El código leído es demasiado largo (más de {EscanerRemotoLimites.LONGITUD_MAXIMA_CODIGO} caracteres).");
            }
            if (lectura.ClaveIdempotencia != null && lectura.ClaveIdempotencia.Trim().Length > EscanerRemotoLimites.LONGITUD_MAXIMA_CLAVE)
            {
                return BadRequest($"La clave de idempotencia es demasiado larga (más de {EscanerRemotoLimites.LONGITUD_MAXIMA_CLAVE} caracteres).");
            }
            lectura.Codigo = codigo;
            return Ok(servicio.Enviar(usuario, lectura));
        }

        private IHttpActionResult Rechazo(out string usuario)
        {
            usuario = User?.Identity?.IsAuthenticated == true ? User.Identity.Name : null;
            if (string.IsNullOrWhiteSpace(UsuarioEscanerRemoto.SinDominio(usuario)))
            {
                return Unauthorized();
            }
            if (EscanerRemotoPermisos.EsCliente(User))
            {
                return Content(HttpStatusCode.Forbidden, new HttpError(MENSAJE_CLIENTE));
            }
            return null;
        }
    }
}
