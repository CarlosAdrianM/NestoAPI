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
    /// NestoAPI#591 (MVP): eventos (cursos, masterclass…) con señal reembolsable. Leer basta con estar identificado; las señales
    /// las marca y desmarca Administración (y Dirección/Informática). 403 si no. Los errores de negocio (apunte ajeno, ya
    /// marcado…) salen como 400 por el filtro global.
    /// <para>NestoAPI#611: cada evento es de una tienda (REI o ALC) y lo mantiene esa tienda (grupo Tiendas con ese
    /// AlmacénPedidoVta) o Dirección: crear, modificar, marcar pagos y pasar lista. Inscribir puede cualquiera de la casa y el
    /// cliente de la tienda online a sí mismo (<see cref="PermisosEventos"/>).</para>
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Eventos")]
    public class EventosController : ApiController
    {
        private readonly NVEntities db;
        private readonly IServicioEventos servicio;
        private readonly IServicioInscripcionesEventos inscripciones;

        public EventosController() : this(new NVEntities())
        {
        }

        internal EventosController(NVEntities db, IServicioEventos servicio = null, IServicioInscripcionesEventos inscripciones = null)
        {
            this.db = db;
            this.servicio = servicio ?? new ServicioEventos(db);
            this.inscripciones = inscripciones ?? new ServicioInscripcionesEventos(db);
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

        // POST api/Eventos   { Empresa, Titulo, Fecha, ImporteSenal, Activo, Almacen?, ProductoCurso?, Plazas?, Lugar?, DescripcionLarga? }
        /// <summary>NestoAPI#611: con Almacen, esa tienda, Administración o Dirección; sin él, como en #591 (Tienda online, Dirección).</summary>
        [HttpPost]
        [Route("")]
        [ResponseType(typeof(EventoDTO))]
        public async Task<IHttpActionResult> PostEvento([FromBody] EventoDTO evento)
        {
            if (!PuedeMantener(evento?.Empresa, evento?.Almacen))
            {
                return Prohibido(PermisosEventos.MENSAJE_SIN_PERMISO_EVENTOS);
            }
            return Ok(await servicio.CrearEvento(evento, UsuarioAuditoriaHelper.Resolver(User, null)).ConfigureAwait(false));
        }

        // PUT api/Eventos/5   { Titulo, Fecha, ImporteSenal, Activo, y solo los campos nuevos que vengan: Almacen, ProductoCurso, Plazas, Lugar, DescripcionLarga }
        /// <summary>NestoAPI#611: quien mantiene el evento con la tienda de antes y con la nueva (si se cambia). Conserva los campos nuevos que no vengan.</summary>
        [HttpPut]
        [Route("{id:int}")]
        [ResponseType(typeof(EventoDTO))]
        public async Task<IHttpActionResult> PutEvento(int id, [FromBody] EventoDTO evento)
        {
            // NestoAPI#611: el Nesto de #591 no manda Almacen: entonces el evento se queda en la tienda que tuviera (o en ninguna).
            string almacenActual = await servicio.AlmacenDelEvento(id).ConfigureAwait(false);
            string almacenNuevo = evento != null && evento.Trae(nameof(EventoDTO.Almacen)) ? evento.Almacen : almacenActual;
            if ((almacenActual != null && !PuedeMantener(evento?.Empresa, almacenActual)) || !PuedeMantener(evento?.Empresa, almacenNuevo))
            {
                return Prohibido(PermisosEventos.MENSAJE_SIN_PERMISO_EVENTOS);
            }
            return Ok(await servicio.ModificarEvento(id, evento, UsuarioAuditoriaHelper.Resolver(User, null)).ConfigureAwait(false));
        }

        // ---------------- NestoAPI#611: inscripciones ----------------

        // POST api/Eventos/5/Inscripciones   { Empresa, Cliente, Contacto?, Origen, FormaPago?, GenerarEnlacePago?, Correo? }
        /// <summary>
        /// Inscribe al cliente (sin pedido). Con tarjeta (o sin forma de pago) y GenerarEnlacePago genera el enlace de pago, que
        /// viene en UrlPago. Cualquiera de la casa; el cliente de la tienda online, solo a sí mismo.
        /// </summary>
        [HttpPost]
        [Route("{id:int}/Inscripciones")]
        [ResponseType(typeof(InscripcionEventoDTO))]
        public async Task<IHttpActionResult> PostInscripcion(int id, [FromBody] InscribirEventoDTO peticion)
        {
            return Ok(await inscripciones.Inscribir(id, peticion, User).ConfigureAwait(false));
        }

        // GET api/Eventos/5/Inscripciones
        /// <summary>Los inscritos del evento (todas, también caducadas y anuladas). Solo la casa.</summary>
        [HttpGet]
        [Route("{id:int}/Inscripciones")]
        [ResponseType(typeof(List<InscripcionEventoDTO>))]
        public async Task<IHttpActionResult> GetInscripciones(int id)
        {
            return Ok(await inscripciones.LeerInscripcionesEvento(id, User).ConfigureAwait(false));
        }

        // DELETE api/Eventos/Inscripciones/7
        /// <summary>Anula una inscripción sin pagar (Reservada). Devuelve cómo queda.</summary>
        [HttpDelete]
        [Route("Inscripciones/{id:int}")]
        [ResponseType(typeof(InscripcionEventoDTO))]
        public async Task<IHttpActionResult> DeleteInscripcion(int id)
        {
            return Ok(await inscripciones.Anular(id, User).ConfigureAwait(false));
        }

        // POST api/Eventos/Inscripciones/7/Pago   { FormaPago, Importe?, Fecha? }
        /// <summary>
        /// Marca la inscripción como pagada a mano (transferencia o tarjeta). Quien mantiene el evento, Administración o
        /// Dirección. Pasa por el mismo enganche del corte 2 que el cobro por enlace. Efectivo: 400, todavía no (falta el cobro en caja).
        /// </summary>
        [HttpPost]
        [Route("Inscripciones/{id:int}/Pago")]
        [ResponseType(typeof(InscripcionEventoDTO))]
        public async Task<IHttpActionResult> PostPagoInscripcion(int id, [FromBody] MarcarPagoInscripcionDTO peticion)
        {
            return Ok(await inscripciones.MarcarPagada(id, peticion, User).ConfigureAwait(false));
        }

        // POST api/Eventos/5/Asistencia   { Asistentes: [7, 9] }
        /// <summary>
        /// Lista de asistencia (desde el día del evento): las pagadas que vienen en Asistentes pasan a Asistió y el resto a No
        /// asistió. La tienda del evento o Dirección. Se puede repetir para corregirla.
        /// </summary>
        [HttpPost]
        [Route("{id:int}/Asistencia")]
        [ResponseType(typeof(List<InscripcionEventoDTO>))]
        public async Task<IHttpActionResult> PostAsistencia(int id, [FromBody] AsistenciaEventoDTO peticion)
        {
            return Ok(await inscripciones.PasarLista(id, peticion, User).ConfigureAwait(false));
        }

        // GET api/Clientes/1/15191/Inscripciones?contacto=0
        /// <summary>Las inscripciones de un cliente (de todos sus contactos si no se pide uno). El cliente de la tienda, solo las suyas.</summary>
        [HttpGet]
        [Route("~/api/Clientes/{empresa}/{cliente}/Inscripciones")]
        [ResponseType(typeof(List<InscripcionEventoDTO>))]
        public async Task<IHttpActionResult> GetInscripcionesCliente(string empresa, string cliente, string contacto = null)
        {
            return Ok(await inscripciones.LeerInscripcionesCliente(empresa, cliente, contacto, User).ConfigureAwait(false));
        }

        private bool PuedeMantener(string empresa, string almacenEvento)
        {
            if (PermisosEventos.EsDireccion(User))
            {
                return true;
            }
            return PermisosEventos.PuedeMantener(User, servicio.AlmacenDelUsuario(User, empresa), almacenEvento);
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
