using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Novedades;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
        private readonly IServicioPropuestasSenales propuestas;

        public EventosController() : this(new NVEntities())
        {
        }

        internal EventosController(NVEntities db, IServicioEventos servicio = null, IServicioInscripcionesEventos inscripciones = null,
            IServicioPropuestasSenales propuestas = null)
        {
            this.db = db;
            this.servicio = servicio ?? new ServicioEventos(db);
            this.inscripciones = inscripciones ?? new ServicioInscripcionesEventos(db);
            this.propuestas = propuestas ?? new ServicioPropuestasSenales(db);
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

        // ---------------- NestoAPI#690: imagen del evento ----------------

        // POST api/Eventos/5/Imagen   multipart/form-data, la imagen en el campo «fichero»
        /// <summary>
        /// NestoAPI#690: pone (o sustituye) la imagen del evento, para las campañas. PNG, JPEG, GIF o WebP hasta 10 MB (las
        /// reglas de los adjuntos de Novedades, #616, sin PDF). La tienda del evento, Tienda online y Dirección. Devuelve el
        /// evento con su ImagenUrl nueva.
        /// </summary>
        [HttpPost]
        [Route("{id:int}/Imagen")]
        [ResponseType(typeof(EventoDTO))]
        public async Task<IHttpActionResult> PostImagen(int id)
        {
            string almacenEvento = await servicio.AlmacenDelEvento(id).ConfigureAwait(false);
            if (almacenEvento == null)
            {
                return NotFound();
            }
            if (!PermisosEventos.PuedeSubirImagen(User, servicio.AlmacenDelUsuario(User, null), almacenEvento))
            {
                return Prohibido(PermisosEventos.MENSAJE_SIN_PERMISO_IMAGEN);
            }
            if (Request.Content == null || !Request.Content.IsMimeMultipartContent())
            {
                return Content(HttpStatusCode.UnsupportedMediaType, new HttpError(
                    $"Envíe la imagen como multipart/form-data en el campo «{ReglasAdjuntosNovedades.CAMPO_FICHERO}»."));
            }
            MultipartMemoryStreamProvider partes = await Request.Content.ReadAsMultipartAsync(new MultipartMemoryStreamProvider()).ConfigureAwait(false);
            var ficheros = new List<(string Nombre, string Tipo, byte[] Contenido)>();
            foreach (HttpContent parte in partes.Contents)
            {
                ContentDispositionHeaderValue disposicion = parte.Headers.ContentDisposition;
                string campo = disposicion?.Name?.Trim('"');
                string nombre = disposicion?.FileNameStar ?? disposicion?.FileName?.Trim('"');
                if (!string.Equals(campo, ReglasAdjuntosNovedades.CAMPO_FICHERO, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(nombre))
                {
                    continue; // otros campos del formulario
                }
                byte[] contenido = await parte.ReadAsByteArrayAsync().ConfigureAwait(false);
                ficheros.Add((nombre, parte.Headers.ContentType?.MediaType, contenido));
            }
            ReglasImagenesEventos.Validacion validacion = ReglasImagenesEventos.Validar(ficheros);
            if (validacion.Error != null)
            {
                return validacion.TipoNoPermitido
                    ? Content(HttpStatusCode.UnsupportedMediaType, new HttpError(validacion.Error))
                    : (IHttpActionResult)BadRequest(validacion.Error);
            }
            return Ok(await servicio.GuardarImagen(id, validacion.Imagen, UsuarioAuditoriaHelper.Resolver(User, null)).ConfigureAwait(false));
        }

        // DELETE api/Eventos/5/Imagen
        /// <summary>NestoAPI#690: quita la imagen del evento (204; 404 si no tenía). Los mismos que pueden ponerla.</summary>
        [HttpDelete]
        [Route("{id:int}/Imagen")]
        public async Task<IHttpActionResult> DeleteImagen(int id)
        {
            string almacenEvento = await servicio.AlmacenDelEvento(id).ConfigureAwait(false);
            if (almacenEvento == null)
            {
                return NotFound();
            }
            if (!PermisosEventos.PuedeSubirImagen(User, servicio.AlmacenDelUsuario(User, null), almacenEvento))
            {
                return Prohibido(PermisosEventos.MENSAJE_SIN_PERMISO_IMAGEN);
            }
            return await servicio.BorrarImagen(id).ConfigureAwait(false)
                ? (IHttpActionResult)StatusCode(HttpStatusCode.NoContent)
                : NotFound();
        }

        // GET api/Eventos/5/Imagen?v=20261022101500
        /// <summary>
        /// NestoAPI#690: la imagen del evento. Anónima, porque la piden los correos y las campañas, y cacheable (pública, 7
        /// días, con ETag: la URL cambia al sustituirla). 404 si no tiene.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        [Route("{id:int}/Imagen")]
        public async Task<IHttpActionResult> GetImagen(int id)
        {
            ImagenEvento imagen = await servicio.LeerImagen(id).ConfigureAwait(false);
            if (imagen?.Contenido == null || imagen.Contenido.Length == 0)
            {
                return NotFound();
            }
            var etag = new EntityTagHeaderValue("\"" + imagen.Fecha.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture) + "\"");
            bool sinCambios = Request?.Headers.IfNoneMatch?.Any(e => e.Tag == "*" || e.Tag == etag.Tag) == true;
            HttpResponseMessage respuesta;
            if (sinCambios)
            {
                respuesta = new HttpResponseMessage(HttpStatusCode.NotModified);
            }
            else
            {
                respuesta = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(imagen.Contenido) };
                respuesta.Content.Headers.ContentType = new MediaTypeHeaderValue(
                    string.IsNullOrWhiteSpace(imagen.Tipo) ? "application/octet-stream" : imagen.Tipo.Trim());
                respuesta.Content.Headers.ContentLength = imagen.Contenido.Length;
            }
            respuesta.Headers.ETag = etag;
            respuesta.Headers.CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromDays(7) };
            return ResponseMessage(respuesta);
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
        /// Marca la inscripción como pagada a mano (transferencia, tarjeta o efectivo). Quien mantiene el evento, Administración
        /// o Dirección. Pasa por el mismo enganche del corte 2 que el cobro por enlace. La API no contabiliza nada: el efectivo
        /// lo mete cada usuario en su caja (Carlos, 15/10/26).
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

        // POST api/Eventos/Inscripciones/7/ConfirmarAsistencia   { Respuesta: "Confirma" | "NoPodraIr" }
        /// <summary>
        /// NestoAPI#690: «Confirmo asistencia» / «No podré ir», independiente del pago. El cliente con su token de la tienda
        /// (solo las suyas) o la tienda del evento, Administración o Dirección. Hasta el día del evento.
        /// </summary>
        [HttpPost]
        [Route("Inscripciones/{id:int}/ConfirmarAsistencia")]
        [ResponseType(typeof(InscripcionEventoDTO))]
        public async Task<IHttpActionResult> PostConfirmarAsistencia(int id, [FromBody] ConfirmarAsistenciaDTO peticion)
        {
            return Ok(await inscripciones.ConfirmarAsistencia(id, peticion, User).ConfigureAwait(false));
        }

        // GET api/Eventos/Asistencia/{clave}?respuesta=Confirma
        /// <summary>
        /// NestoAPI#690: el botón del correo del recordatorio. Sin usuario: el enlace lleva la clave de la inscripción, que no se
        /// puede adivinar. Enseña el evento y un botón; NO apunta nada (los antivirus del correo abren los enlaces).
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        [Route("Asistencia/{clave:guid}")]
        public async Task<IHttpActionResult> GetAsistenciaEnlace(Guid clave, string respuesta = null)
        {
            ResultadoAsistenciaEnlace resultado = await inscripciones.LeerAsistenciaConClave(clave).ConfigureAwait(false);
            return Pagina(resultado, PaginasAsistenciaEvento.Pregunta(resultado, clave, respuesta));
        }

        // POST api/Eventos/Asistencia/{clave}   (formulario: respuesta=Confirma | NoPodraIr)
        /// <summary>NestoAPI#690: apunta la respuesta que llega por el enlace del correo (Origen Cliente).</summary>
        [HttpPost]
        [AllowAnonymous]
        [Route("Asistencia/{clave:guid}")]
        public async Task<IHttpActionResult> PostAsistenciaEnlace(Guid clave)
        {
            string respuesta = null;
            if (Request?.Content != null && Request.Content.IsFormData())
            {
                System.Collections.Specialized.NameValueCollection formulario = await Request.Content.ReadAsFormDataAsync().ConfigureAwait(false);
                respuesta = formulario?["respuesta"];
            }
            respuesta = respuesta ?? Request?.GetQueryNameValuePairs().FirstOrDefault(p => p.Key == "respuesta").Value;
            ResultadoAsistenciaEnlace resultado = await inscripciones.ConfirmarAsistenciaConClave(clave, respuesta).ConfigureAwait(false);
            return Pagina(resultado, PaginasAsistenciaEvento.Resultado(resultado));
        }

        private IHttpActionResult Pagina(ResultadoAsistenciaEnlace resultado, string html)
        {
            var respuesta = new HttpResponseMessage(resultado.Existe ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            };
            return ResponseMessage(respuesta);
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
            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            return Ok(await MarcarSenalYPagar(id, peticion, usuario, resolverPropuestas: ProponerSenales()).ConfigureAwait(false));
        }

        /// <summary>
        /// El camino único de «este apunte es la señal del evento»: lo usan el botón de #591 y la confirmación de una propuesta
        /// (NestoAPI#615), para que confirmar sea exactamente lo mismo que marcarla a mano.
        /// </summary>
        private async Task<SenalEventoDTO> MarcarSenalYPagar(int eventoId, MarcarSenalEventoDTO peticion, string usuario, bool resolverPropuestas)
        {
            SenalEventoDTO senal = await servicio.MarcarSenal(eventoId, peticion, usuario).ConfigureAwait(false);

            // NestoAPI#612: con la facturación de cursos encendida, la señal paga la inscripción del cliente (o la crea, con
            // origen Transferencia) y de ahí sale sola la factura del curso con la señal liquidada. Si falla, la señal queda
            // marcada como hasta hoy (pendiente en la lista de #591) y el error va a ELMAH.
            if (FacturarCursoAlCobrar())
            {
                try
                {
                    _ = await inscripciones.InscribirDesdeTransferencia(senal.Id, usuario).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Cursos #612] La señal {senal.Id} está marcada, pero no se ha podido pagar con ella la " +
                        $"inscripción del cliente {senal.Cliente}: {ex.Message}", ex));
                }
            }

            // NestoAPI#615: las propuestas de ese apunte quedan resueltas (la del evento elegido, confirmada; las demás, descartadas).
            if (resolverPropuestas)
            {
                try
                {
                    await propuestas.Resolver(senal.Empresa ?? peticion?.Empresa, senal.NumOrdenExtracto, senal.EventoId, usuario).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Señales #615] La señal {senal.Id} está marcada, pero no se han podido cerrar las propuestas " +
                        $"del apunte {senal.NumOrdenExtracto}: {ex.Message}", ex));
                }
            }
            return senal;
        }

        /// <summary>NestoAPI#612: el interruptor (Web.config Eventos:FacturarCursoAlCobrar). Sustituible en tests.</summary>
        internal Func<bool> FacturarCursoAlCobrar { get; set; } = () => InterruptoresEventos.FacturarCursoAlCobrar;

        /// <summary>NestoAPI#615: el interruptor (Web.config Eventos:ProponerSenales). Sustituible en tests.</summary>
        internal Func<bool> ProponerSenales { get; set; } = () => InterruptorPropuestasSenales.Encendido;

        // ---------------- NestoAPI#615: señales propuestas ----------------

        // GET api/Eventos/Senales/Propuestas?empresa=1
        /// <summary>
        /// Los cobros a favor que pueden ser la señal de un evento, sin confirmar, con sus eventos candidatos (uno o varios).
        /// No salen los que ya son señal ni los que ya no tienen nada a favor.
        /// </summary>
        [HttpGet]
        [Route("Senales/Propuestas")]
        [ResponseType(typeof(List<PropuestaSenalDTO>))]
        public async Task<IHttpActionResult> GetPropuestasSenales(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return Ok(await propuestas.LeerPendientes(empresa).ConfigureAwait(false));
        }

        // POST api/Eventos/Senales/Propuestas/Buscar   { Empresa?, Dias? }
        /// <summary>
        /// Busca ahora (sin esperar al barrido de la mañana) en los cobros a cuenta de los últimos días (15 si no se dice) y
        /// devuelve las propuestas pendientes. Administración, Dirección e Informática. Funciona con el interruptor apagado:
        /// solo propone, no marca nada.
        /// </summary>
        [HttpPost]
        [Route("Senales/Propuestas/Buscar")]
        [ResponseType(typeof(List<PropuestaSenalDTO>))]
        public async Task<IHttpActionResult> PostBuscarPropuestasSenales([FromBody] BuscarPropuestasSenalesDTO peticion)
        {
            if (!ServicioEventos.PuedeMarcarSenales(User))
            {
                return Prohibido(ServicioEventos.MENSAJE_SIN_PERMISO_SENALES);
            }
            string empresa = string.IsNullOrWhiteSpace(peticion?.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : peticion.Empresa.Trim();
            int dias = peticion?.Dias ?? ServicioPropuestasSenales.DIAS_BARRIDO;
            if (dias < 1 || dias > 90)
            {
                throw new NestoBusinessException("Indique entre 1 y 90 días.");
            }
            _ = await propuestas.ProponerRecientes(empresa, Hoy().AddDays(-dias)).ConfigureAwait(false);
            return Ok(await propuestas.LeerPendientes(empresa).ConfigureAwait(false));
        }

        /// <summary>NestoAPI#615: sustituible en tests.</summary>
        internal Func<DateTime> Hoy { get; set; } = () => DateTime.Today;

        // POST api/Eventos/Senales/Propuestas/12/Confirmar
        /// <summary>
        /// Confirma la propuesta: marca el apunte como señal de ese evento por el mismo camino que «Es la señal del evento…»
        /// (y, con el corte 2 encendido, paga la inscripción, factura el curso y emite el cupón). Las otras propuestas del mismo
        /// apunte quedan descartadas. Administración, Dirección e Informática. Devuelve la señal. 404 si no existe; 409 si ya
        /// estaba confirmada o descartada; 400 con el motivo si el apunte ya no puede ser señal.
        /// </summary>
        [HttpPost]
        [Route("Senales/Propuestas/{id:int}/Confirmar")]
        [ResponseType(typeof(SenalEventoDTO))]
        public async Task<IHttpActionResult> PostConfirmarPropuestaSenal(int id)
        {
            if (!ServicioEventos.PuedeMarcarSenales(User))
            {
                return Prohibido(ServicioEventos.MENSAJE_SIN_PERMISO_SENALES);
            }
            PropuestaSenalEvento propuesta = await LeerPropuestaPendiente(id).ConfigureAwait(false);
            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            var peticion = new MarcarSenalEventoDTO
            {
                Empresa = propuesta.Empresa,
                NumOrdenExtracto = propuesta.NumOrdenExtracto,
                Cliente = propuesta.Cliente,
                Contacto = propuesta.Contacto
            };
            return Ok(await MarcarSenalYPagar(propuesta.EventoId, peticion, usuario, resolverPropuestas: true).ConfigureAwait(false));
        }

        // POST api/Eventos/Senales/Propuestas/12/Descartar
        /// <summary>No es la señal de ese evento: no se vuelve a proponer. Administración, Dirección e Informática. 204.</summary>
        [HttpPost]
        [Route("Senales/Propuestas/{id:int}/Descartar")]
        public async Task<IHttpActionResult> PostDescartarPropuestaSenal(int id)
        {
            if (!ServicioEventos.PuedeMarcarSenales(User))
            {
                return Prohibido(ServicioEventos.MENSAJE_SIN_PERMISO_SENALES);
            }
            _ = await LeerPropuestaPendiente(id).ConfigureAwait(false);
            if (!await propuestas.Descartar(id, UsuarioAuditoriaHelper.Resolver(User, null)).ConfigureAwait(false))
            {
                throw new NestoBusinessException("Otra persona acaba de confirmar o descartar esta propuesta.") { StatusCode = HttpStatusCode.Conflict };
            }
            return StatusCode(HttpStatusCode.NoContent);
        }

        private async Task<PropuestaSenalEvento> LeerPropuestaPendiente(int id)
        {
            PropuestaSenalEvento propuesta = await propuestas.LeerPropuesta(id).ConfigureAwait(false)
                ?? throw new NestoBusinessException($"No existe la propuesta {id}.") { StatusCode = HttpStatusCode.NotFound };
            string estado = propuesta.Estado?.Trim();
            if (estado != EstadosPropuestaSenal.PROPUESTA)
            {
                string como = estado == EstadosPropuestaSenal.CONFIRMADA ? "confirmada" : "descartada";
                string quien = string.IsNullOrWhiteSpace(propuesta.Usuario) ? string.Empty : " por " + propuesta.Usuario.Trim();
                throw new NestoBusinessException($"Esta propuesta ya está {como}{quien}.") { StatusCode = HttpStatusCode.Conflict };
            }
            return propuesta;
        }

        // POST api/Eventos/Inscripciones/7/Facturar
        /// <summary>
        /// NestoAPI#612: relanza la facturación del curso (pedido, factura exenta, liquidación de la señal) desde donde se
        /// quedó; lo hecho no se repite. La tienda del evento, Administración o Dirección. 400 con el motivo si no se puede
        /// automatizar o falla (o si el interruptor está apagado); 409 si la está haciendo otro proceso en ese momento.
        /// </summary>
        [HttpPost]
        [Route("Inscripciones/{id:int}/Facturar")]
        [ResponseType(typeof(InscripcionEventoDTO))]
        public async Task<IHttpActionResult> PostFacturarCurso(int id)
        {
            return Ok(await inscripciones.FacturarCurso(id, User).ConfigureAwait(false));
        }

        // POST api/Eventos/CursoTienda   { Empresa, Cliente, Contacto?, Producto, Importe, Fecha?, ReferenciaTienda?, FormaPago? }
        /// <summary>
        /// NestoAPI#614: Nesto (CanalesExternos) ha importado de la tienda PrestaShop la compra de un curso. En vez de facturarla como
        /// pedido normal, paga la inscripción del cliente (la crea con origen PrestaShop si no la tenía) y sigue el corte 2. 400 con
        /// el interruptor Eventos:CursosDesdePrestashop apagado o con dos eventos con la misma referencia; 404 si no hay evento activo
        /// y por venir con esa referencia; 403 para el cliente de la tienda.
        /// </summary>
        [HttpPost]
        [Route("CursoTienda")]
        [ResponseType(typeof(InscripcionEventoDTO))]
        public async Task<IHttpActionResult> PostCursoTienda([FromBody] CursoTiendaDTO peticion)
        {
            return Ok(await inscripciones.InscribirDesdeTienda(peticion, User).ConfigureAwait(false));
        }

        // DELETE api/Eventos/Senales/7
        /// <summary>
        /// Quita la señal (el apunte vuelve a ser un cobro a cuenta normal). No toca el extracto. NestoAPI#612: si ya facturó el
        /// curso o emitió el cupón, 409 SENAL_EVENTO_YA_USADA diciendo qué falta (la rectificativa del curso, anular el cupón; si el
        /// cupón ya se usó, no se puede). Con la rectificativa hecha y el cupón anulado (o sin ellos), se quita. Si pagó una
        /// inscripción, se suelta de ella (vuelve a pendiente de pago) y se borra, en una transacción.
        /// </summary>
        [HttpDelete]
        [Route("Senales/{id:int}")]
        public async Task<IHttpActionResult> DeleteSenal(int id)
        {
            if (!ServicioEventos.PuedeMarcarSenales(User))
            {
                return Prohibido(ServicioEventos.MENSAJE_SIN_PERMISO_SENALES);
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            await servicio.QuitarSenal(id, usuario).ConfigureAwait(false);
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
