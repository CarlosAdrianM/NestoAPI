using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Models;
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
    /// NestoAPI#591 (MVP): eventos (cursos, masterclass…) con señal reembolsable. Leer basta con estar identificado; los eventos
    /// los mantiene Tienda online (y Dirección/Informática) y las señales las marca y desmarca Administración (y
    /// Dirección/Informática). 403 si no. Los errores de negocio (apunte ajeno, ya marcado…) salen como 400 por el filtro global.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Eventos")]
    public class EventosController : ApiController
    {
        private readonly NVEntities db;
        private readonly IServicioEventos servicio;

        public EventosController() : this(new NVEntities())
        {
        }

        internal EventosController(NVEntities db, IServicioEventos servicio = null)
        {
            this.db = db;
            this.servicio = servicio ?? new ServicioEventos(db);
        }

        // GET api/Eventos?empresa=1&soloActivos=true
        /// <summary>Los eventos: de hoy en adelante primero (el más cercano antes) y luego los pasados.</summary>
        [HttpGet]
        [Route("")]
        [ResponseType(typeof(List<EventoDTO>))]
        public async Task<IHttpActionResult> GetEventos(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, bool soloActivos = true)
        {
            return Ok(await servicio.LeerEventos(empresa, soloActivos).ConfigureAwait(false));
        }

        // POST api/Eventos   { Empresa, Titulo, Fecha, ImporteSenal, Activo }
        [HttpPost]
        [Route("")]
        [ResponseType(typeof(EventoDTO))]
        public async Task<IHttpActionResult> PostEvento([FromBody] EventoDTO evento)
        {
            if (!ServicioEventos.PuedeMantenerEventos(User))
            {
                return Prohibido(ServicioEventos.MENSAJE_SIN_PERMISO_EVENTOS);
            }
            return Ok(await servicio.CrearEvento(evento, UsuarioAuditoriaHelper.Resolver(User, null)).ConfigureAwait(false));
        }

        // PUT api/Eventos/5   { Titulo, Fecha, ImporteSenal, Activo }
        [HttpPut]
        [Route("{id:int}")]
        [ResponseType(typeof(EventoDTO))]
        public async Task<IHttpActionResult> PutEvento(int id, [FromBody] EventoDTO evento)
        {
            if (!ServicioEventos.PuedeMantenerEventos(User))
            {
                return Prohibido(ServicioEventos.MENSAJE_SIN_PERMISO_EVENTOS);
            }
            return Ok(await servicio.ModificarEvento(id, evento, UsuarioAuditoriaHelper.Resolver(User, null)).ConfigureAwait(false));
        }

        // GET api/Eventos/Senales?empresa=1&estado=Liberada&cliente=15191&eventoId=3
        /// <summary>Las señales con su estado calculado. estado: Pendiente, Liberada, SinCompra («Sin compra»), Consumida; vacío o «Todas» = todas.</summary>
        [HttpGet]
        [Route("Senales")]
        [ResponseType(typeof(List<SenalEventoDTO>))]
        public async Task<IHttpActionResult> GetSenales(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, string estado = null,
            string cliente = null, int? eventoId = null)
        {
            return Ok(await servicio.LeerSenales(empresa, estado, cliente, eventoId).ConfigureAwait(false));
        }

        // GET api/Eventos/Senales/Cliente?empresa=1&cliente=15191&contacto=0
        /// <summary>Las señales de un cliente (para pintar en su extracto qué apuntes son señal de un evento).</summary>
        [HttpGet]
        [Route("Senales/Cliente")]
        [ResponseType(typeof(List<SenalEventoDTO>))]
        public async Task<IHttpActionResult> GetSenalesCliente(string cliente, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
            string contacto = null)
        {
            return Ok(await servicio.LeerSenalesCliente(empresa, cliente, contacto).ConfigureAwait(false));
        }

        // POST api/Eventos/5/Senales   { Empresa, NumOrdenExtracto, Cliente?, Contacto? }
        /// <summary>Marca un apunte a favor del extracto como la señal del evento. 400 si no es del cliente, no está a favor o ya es señal.</summary>
        [HttpPost]
        [Route("{id:int}/Senales")]
        [ResponseType(typeof(SenalEventoDTO))]
        public async Task<IHttpActionResult> PostSenal(int id, [FromBody] MarcarSenalEventoDTO peticion)
        {
            if (!ServicioEventos.PuedeMarcarSenales(User))
            {
                return Prohibido(ServicioEventos.MENSAJE_SIN_PERMISO_SENALES);
            }
            return Ok(await servicio.MarcarSenal(id, peticion, UsuarioAuditoriaHelper.Resolver(User, null)).ConfigureAwait(false));
        }

        // DELETE api/Eventos/Senales/7
        /// <summary>Quita la señal (el apunte vuelve a ser un cobro a cuenta normal). No toca el extracto.</summary>
        [HttpDelete]
        [Route("Senales/{id:int}")]
        public async Task<IHttpActionResult> DeleteSenal(int id)
        {
            if (!ServicioEventos.PuedeMarcarSenales(User))
            {
                return Prohibido(ServicioEventos.MENSAJE_SIN_PERMISO_SENALES);
            }
            await servicio.QuitarSenal(id).ConfigureAwait(false);
            return StatusCode(HttpStatusCode.NoContent);
        }

        /// <summary>Identificado pero sin permiso: 403, no 401 (con 401 el cliente intentaría refrescar el token).</summary>
        private IHttpActionResult Prohibido(string mensaje)
        {
            return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.Forbidden, mensaje));
        }

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
