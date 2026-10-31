using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Infraestructure;
using NestoAPI.Infrastructure;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#773 (Carlos 31/10/26): el cobro con tarjeta desde la PDA (enlace Redsys por WhatsApp o correo, para el reembolso de un envío
    /// o para efectos de un cliente) y la autorización para entregar un reembolso sin cobrar. Mismo prefijo que <see cref="RepartoPropioController"/>
    /// (api/RepartoPropio); aparte para no engordarlo. Contrato en la issue.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/RepartoPropio")]
    public class RepartoPropioCobrosController : ApiController
    {
        /// <summary>Quién contesta las autorizaciones: Administración y Dirección.</summary>
        public static readonly IReadOnlyList<string> GRUPOS_AUTORIZAN = new[]
        {
            Constantes.GruposSeguridad.ADMINISTRACION, Constantes.GruposSeguridad.DIRECCION
        };

        private readonly NVEntities db;
        private readonly ServicioCobroTarjetaReparto tarjeta;
        private readonly ServicioAutorizacionesReembolso autorizaciones;

        public RepartoPropioCobrosController() : this(new NVEntities())
        {
        }

        internal RepartoPropioCobrosController(NVEntities db)
            : this(db, new ServicioCobroTarjetaReparto(new RepositorioRepartoPropio(db), new RepositorioCobrosTarjetaReparto(db)),
                new ServicioAutorizacionesReembolso(new RepositorioRepartoPropio(db), new RepositorioAutorizacionesReembolso(db)))
        {
        }

        internal RepartoPropioCobrosController(NVEntities db, ServicioCobroTarjetaReparto tarjeta, ServicioAutorizacionesReembolso autorizaciones)
        {
            this.db = db;
            this.tarjeta = tarjeta;
            this.autorizaciones = autorizaciones;
        }

        // POST api/RepartoPropio/Envios/251300/EnlaceTarjeta { Correo }
        /// <summary>
        /// El enlace con tarjeta del reembolso del envío (el vivo si ya hay uno: nunca dos). Con Correo, se manda también por correo. 400 en
        /// la empresa 3 (siempre efectivo), sin reembolso o sin el script; 409 si ya está entregado o no está en reparto.
        /// </summary>
        [HttpPost]
        [Route("Envios/{envioId:int}/EnlaceTarjeta")]
        [ResponseType(typeof(EnlaceTarjetaRepartoDTO))]
        public async Task<IHttpActionResult> PostEnlaceTarjeta(int envioId, [FromBody] PeticionEnlaceTarjetaDTO peticion)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido("Su usuario no tiene permiso para el reparto propio.");
            }
            return Responder(await tarjeta.EnlaceEnvio(envioId, peticion, Usuario(), Cabecera(Infraestructure.Notificaciones.AvisosAlmacen.CABECERA_DISPOSITIVO)));
        }

        // POST api/RepartoPropio/Cobros/EnlaceTarjeta { Cliente, Contacto, Efectos: [..], Correo }
        /// <summary>El enlace con tarjeta por la suma de los efectos elegidos (al menos uno, pendientes, empresa 1).</summary>
        [HttpPost]
        [Route("Cobros/EnlaceTarjeta")]
        [ResponseType(typeof(EnlaceTarjetaRepartoDTO))]
        public async Task<IHttpActionResult> PostEnlaceTarjetaDeuda([FromBody] PeticionEnlaceTarjetaDeudaDTO peticion)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido("Su usuario no tiene permiso para el reparto propio.");
            }
            return Responder(await tarjeta.EnlaceDeuda(peticion, Usuario(), Cabecera(Infraestructure.Notificaciones.AvisosAlmacen.CABECERA_DISPOSITIVO)));
        }

        // GET api/RepartoPropio/EnlaceTarjeta/4321/Estado
        /// <summary>De respaldo del aviso por SignalR: la PDA lo pregunta al volver a la pantalla o al reconectar (nunca con temporizador).</summary>
        [HttpGet]
        [Route("EnlaceTarjeta/{pagoTPVId:int}/Estado")]
        [ResponseType(typeof(EstadoEnlaceTarjetaDTO))]
        public async Task<IHttpActionResult> GetEstadoEnlaceTarjeta(int pagoTPVId)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido("Su usuario no tiene permiso para el reparto propio.");
            }
            return Responder(await tarjeta.Estado(pagoTPVId));
        }

        // POST api/RepartoPropio/Envios/251300/Autorizacion { Motivo }
        /// <summary>Pide a Administración (y Dirección) entregar sin cobrar el reembolso: aviso en la campana de Nesto.</summary>
        [HttpPost]
        [Route("Envios/{envioId:int}/Autorizacion")]
        [ResponseType(typeof(AutorizacionReembolsoDTO))]
        public async Task<IHttpActionResult> PostAutorizacion(int envioId, [FromBody] PeticionAutorizacionReembolsoDTO peticion)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido("Su usuario no tiene permiso para el reparto propio.");
            }
            return Responder(await autorizaciones.Pedir(envioId, peticion, Usuario()));
        }

        // GET api/RepartoPropio/Autorizaciones/12
        [HttpGet]
        [Route("Autorizaciones/{id:int}")]
        [ResponseType(typeof(AutorizacionReembolsoDTO))]
        public async Task<IHttpActionResult> GetAutorizacion(int id)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User) && !PuedeAutorizar(User))
            {
                return Prohibido("Su usuario no tiene permiso para el reparto propio.");
            }
            return Responder(await autorizaciones.Leer(id));
        }

        // POST api/RepartoPropio/Autorizaciones/12/Responder { Autorizar, Comentario }
        /// <summary>Solo Administración y Dirección. 409 si ya está contestada (por otro, o por teléfono).</summary>
        [HttpPost]
        [Route("Autorizaciones/{id:int}/Responder")]
        [ResponseType(typeof(AutorizacionReembolsoDTO))]
        public async Task<IHttpActionResult> PostResponderAutorizacion(int id, [FromBody] RespuestaAutorizacionReembolsoDTO respuesta)
        {
            if (!PuedeAutorizar(User))
            {
                return Prohibido("Solo Administración o Dirección pueden autorizar que un reembolso se quede sin cobrar.");
            }
            return Responder(await autorizaciones.Responder(id, respuesta, Usuario()));
        }

        public static bool PuedeAutorizar(System.Security.Principal.IPrincipal usuario)
            => usuario != null && GRUPOS_AUTORIZAN.Any(g => usuario.IsInRoleSinDominio(g));

        private string Usuario() => UsuarioAuditoriaHelper.Resolver(User, "NestoAPI");

        private IHttpActionResult Responder<T>(ResultadoReparto<T> resultado)
        {
            switch (resultado.Tipo)
            {
                case TipoResultadoReparto.Ok:
                    return Ok(resultado.Contenido);
                case TipoResultadoReparto.SinContenido:
                    return StatusCode(HttpStatusCode.NoContent);
                case TipoResultadoReparto.NoEncontrado:
                    return Content(HttpStatusCode.NotFound, new { Message = resultado.Mensaje });
                case TipoResultadoReparto.Conflicto:
                    return Content(HttpStatusCode.Conflict, new { Message = resultado.Mensaje });
                default:
                    return Content(HttpStatusCode.BadRequest, new { Message = resultado.Mensaje });
            }
        }

        private string Cabecera(string nombre)
            => Request != null && Request.Headers.TryGetValues(nombre, out IEnumerable<string> valores)
                ? valores.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim()
                : null;

        private IHttpActionResult Prohibido(string mensaje) => Content(HttpStatusCode.Forbidden, new { Message = mensaje });

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
