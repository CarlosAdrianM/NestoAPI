using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#553: reposición de tiendas desde Nesto y Ariadna (sacarla de Nesto viejo), por fases. Fase 1: la propuesta
    /// (lectura) y crear, preparar y terminar la reposición escribiendo lo mismo que Nesto viejo. Leer basta con estar
    /// identificado; escribir (crear, cambiar cantidades, terminar) lo puede hacer quien tiene el almacén de origen en
    /// AlmacénPedidoVta, Almacén o Dirección (403 si no). Desde un origen con control de ubicaciones (Algete), solo
    /// Almacén y Dirección: el POST reserva los huecos (puerta única de Ubicaciones, NestoAPI#594) y la deja cerrada, lista
    /// para recoger en Ariadna; el DELETE la anula mientras nadie la haya empezado a recoger.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Reposiciones")]
    public class ReposicionesController : ApiController
    {
        private readonly NVEntities db;
        private readonly IServicioPreparacionReposicion preparacion;
        private readonly IServicioTransitoReposiciones transito;
        private readonly IServicioCalendarioReposiciones calendario;
        private readonly IServicioReposicionAutomatica automatica;

        /// <summary>
        /// AddControllersAsServices elige el constructor público que puede construir entero: este.
        /// </summary>
        /// <param name="fichas">NestoAPI#653: la foto de cada producto que ve Ariadna (el único sitio que la pone).</param>
        public ReposicionesController(IFichasProductoAlmacen fichas) : this(new NVEntities())
        {
            this.fichas = fichas;
        }

        internal ReposicionesController(NVEntities db) : this(db, new ServicioPreparacionReposicion(db))
        {
        }

        internal ReposicionesController(NVEntities db, IServicioPreparacionReposicion preparacion,
            IServicioTransitoReposiciones transito = null, IServicioCalendarioReposiciones calendario = null,
            IServicioReposicionAutomatica automatica = null, IPermisoRellenarReposicionManual permisoManual = null,
            IServicioListadoReposicion listado = null, IProductosReposicionPuntual productosPuntual = null,
            IServicioDiferenciasReposicion diferencias = null, Infraestructure.PreparacionAlmacen.IServicioBultosReposicion bultos = null,
            IFichasProductoAlmacen fichas = null)
        {
            this.fichas = fichas; // null = sin foto (NestoAPI#653)
            this.bultos = bultos; // ídem (NestoAPI#645)
            this.diferencias = diferencias; // ídem (NestoAPI#634)
            this.db = db;
            this.preparacion = preparacion;
            this.transito = transito; // null = el de la BD, creado al usarlo (los tests del resto no pasan db)
            this.calendario = calendario; // ídem
            this.automatica = automatica; // ídem
            this.permisoManual = permisoManual; // ídem
            this.listado = listado; // ídem
            this.productosPuntual = productosPuntual; // ídem
        }

        private readonly IProductosReposicionPuntual productosPuntual;

        private readonly IFichasProductoAlmacen fichas;

        /// <summary>NestoAPI#653 (Ariadna#35): la foto de todos los productos de la respuesta, en una sola llamada.</summary>
        private Task CompletarFotos(IEnumerable<IConFotoProducto> productos)
        {
            return fichas == null || productos == null ? Task.CompletedTask : fichas.CompletarFotos(productos);
        }

        /// <summary>
        /// NestoAPI#681: la foto y la referencia del proveedor principal de todos los productos de la respuesta (una llamada
        /// para cada cosa, para todos a la vez).
        /// </summary>
        private Task CompletarFotosYReferencias<T>(string empresa, IEnumerable<T> productos) where T : IConFotoProducto, IConReferenciaProveedor
        {
            if (fichas == null || productos == null)
            {
                return Task.CompletedTask;
            }
            List<T> lista = productos.ToList();
            return Task.WhenAll(
                fichas.CompletarFotos(lista.Cast<IConFotoProducto>()),
                fichas.CompletarReferenciasProveedor(string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim(),
                    lista.Cast<IConReferenciaProveedor>()));
        }

        private async Task<ReposicionEnPreparacionDTO> ConFotos(ReposicionEnPreparacionDTO reposicion)
        {
            // Ariadna#47: con los demás productos del hueco de cada línea (solo las que salen de un hueco: desde Algete)
            // Ariadna#22 (NestoAPI#605): y todos los códigos de barras de cada producto (una consulta para todas las líneas)
            await Task.WhenAll(
                CompletarFotosYReferencias(reposicion?.Empresa, reposicion?.Lineas),
                fichas == null || reposicion?.Lineas == null
                    ? Task.CompletedTask
                    : fichas.CompletarCodigos(string.IsNullOrWhiteSpace(reposicion.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : reposicion.Empresa.Trim(),
                        reposicion.Lineas),
                fichas == null || reposicion?.Lineas == null || string.IsNullOrWhiteSpace(reposicion.Origen)
                    ? Task.CompletedTask
                    : fichas.CompletarMismoHueco(reposicion.Empresa, reposicion.Origen, reposicion.Lineas)).ConfigureAwait(false);
            return reposicion;
        }

        private IProductosReposicionPuntual ProductosPuntual => productosPuntual ?? new ProductosReposicionPuntual(db);

        // GET api/Reposiciones/Puntual/Producto?origen=ALC&codigo=8437017506362&empresa=1
        /// <summary>
        /// Sugerencia 581: lo que se lee (o teclea) en la pantalla de la reposición puntual: el producto con esa referencia o,
        /// si no, los que tienen ese código de barras (cualquiera de sus códigos), con el stock en <paramref name="origen"/> y
        /// las unidades que vale una lectura. Lista vacía si no hay ninguno; varios si el código lo comparten (que elija).
        /// </summary>
        [HttpGet]
        [Route("Puntual/Producto")]
        [ResponseType(typeof(List<ProductoReposicionPuntualDTO>))]
        public async Task<IHttpActionResult> GetProductoPuntual(string origen, string codigo, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return Ok(await ProductosPuntual.Buscar(empresa, origen, codigo).ConfigureAwait(false));
        }

        // GET api/Reposiciones/Puntual/Estado?origen=ALC&empresa=1
        /// <summary>
        /// Sugerencia 581 (Carlos, 13/10/26): si hoy se puede hacer la reposición puntual de <paramref name="origen"/> a Algete y,
        /// si no, el motivo (el mismo texto que daría el 409): hoy no hay viaje, la de hoy aún no se ha rellenado, no se ha
        /// enviado o el repartidor ya se la ha llevado (con quién lo marcó y cuándo). También si se puede marcar la recogida.
        /// </summary>
        [HttpGet]
        [Route("Puntual/Estado")]
        [ResponseType(typeof(EstadoReposicionPuntualDTO))]
        public async Task<IHttpActionResult> GetEstadoPuntual(string origen, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return Ok(await preparacion.LeerEstadoPuntual(empresa, origen).ConfigureAwait(false));
        }

        // POST api/Reposiciones/Recogida   { Empresa, Origen, Destino? (ALG) }
        /// <summary>
        /// Sugerencia 581 (Carlos, 13/10/26): el repartidor ya se ha llevado la reposición de hoy de la tienda a Algete (lo marca
        /// la tienda desde Nesto; más adelante podrá marcarlo el repartidor desde Ariadna con un usuario de Almacén). Apunta quién
        /// y cuándo y, desde ese momento, la puntual de hoy da 409. Devuelve el estado nuevo. 409 si hoy no hay viaje, la de hoy
        /// no está rellena o enviada, ya estaba marcada o falta el script; 403 sin permiso. Ver
        /// <see cref="ServicioPreparacionReposicion.MarcarRecogida"/>.
        /// </summary>
        [HttpPost]
        [Route("Recogida")]
        [ResponseType(typeof(EstadoReposicionPuntualDTO))]
        public async Task<IHttpActionResult> PostRecogida([FromBody] MarcarRecogidaReposicionDTO peticion)
        {
            if (peticion == null)
            {
                return BadRequest("Falta la reposición de la que se marca la recogida.");
            }
            try
            {
                return Ok(await preparacion.MarcarRecogida(peticion, User).ConfigureAwait(false));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Prohibido(ex);
            }
        }

        // POST api/Reposiciones/Puntual   { Empresa, Origen, Destino? (ALG), Lineas: [{ Producto, Cantidad }] }
        /// <summary>
        /// Sugerencia 581 (Paloma): una reposición PUNTUAL de una tienda a Algete con solo los productos que dice la petición,
        /// aparte de la del calendario, para cuando alguien pide que se mande algo concreto después de la hora de cierre. Se
        /// crea y se termina de una vez: devuelve el traspaso como Terminar, ya pendiente de recibir en Algete. No necesita estar
        /// en «UsuariosRellenarReposicionManual»: basta con tener esa tienda como almacén de pedidos (o Almacén y Dirección);
        /// 403 si no. 409 si hoy no hay viaje a Algete, la de hoy aún no se ha rellenado o no se ha enviado, el repartidor ya se
        /// la ha llevado (POST Recogida) o hay un inventario en curso; 400 si falta
        /// stock en la tienda, si no hay líneas o si no es de una tienda a Algete. Ver
        /// <see cref="ServicioPreparacionReposicion.CrearPuntual"/>.
        /// </summary>
        [HttpPost]
        [Route("Puntual")]
        [ResponseType(typeof(ResultadoTerminarReposicionDTO))]
        public async Task<IHttpActionResult> PostPuntual([FromBody] CrearReposicionDTO peticion)
        {
            if (peticion == null)
            {
                return BadRequest("Faltan los productos de la reposición puntual.");
            }
            try
            {
                ResultadoTerminarReposicionDTO resultado = await preparacion.CrearPuntual(peticion, User).ConfigureAwait(false);
                return Content(HttpStatusCode.Created, resultado);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Prohibido(ex);
            }
        }

        private readonly IServicioListadoReposicion listado;

        private IServicioListadoReposicion Listado => listado ?? new ServicioListadoReposicion(db);

        private readonly IPermisoRellenarReposicionManual permisoManual;

        private IPermisoRellenarReposicionManual PermisoManual => permisoManual ?? new PermisoRellenarReposicionManual(db);

        // GET api/Reposiciones/PuedeRellenarManual
        /// <summary>
        /// NestoAPI#577 (corte 3b): si el usuario actual puede crear (rellenar) reposiciones a mano con POST api/Reposiciones
        /// (parámetro «UsuariosRellenarReposicionManual» de la fila «(defecto)»; hoy Manuel, Alfredo y Carlos). Para que Nesto
        /// y Ariadna escondan el botón a los demás.
        /// </summary>
        [HttpGet]
        [Route("PuedeRellenarManual")]
        [ResponseType(typeof(bool))]
        public IHttpActionResult GetPuedeRellenarManual()
        {
            return Ok(PermisoManual.Puede(User));
        }

        private IServicioCalendarioReposiciones Calendario => calendario ?? new ServicioCalendarioReposiciones(db);

        // POST api/Reposiciones/RellenarAutomatica?origen=REI&destino=ALG&empresa=1
        /// <summary>
        /// NestoAPI#577 (corte 3b): relanza a mano la reposición automática de hoy de una ruta, con el corte del calendario
        /// (el último de hoy que ya ha pasado), como si fuera el job: Herramienta «Automatico», sin líneas (la propuesta con
        /// el corte) y con el usuario de quien lo lanza. Solo Almacén, Dirección e Informática (403). 404 si hoy no toca esa
        /// ruta; 409 si aún no ha llegado la hora de cierre, si ya está rellena o si el origen ya tiene una en preparación que
        /// no se puede completar. 200 con el resultado («Creada», «Completada» —NestoAPI#622: sumada a la que la tienda tenía
        /// sin enviar— o «Vacia») en los demás casos.
        /// </summary>
        [HttpPost]
        [Route("RellenarAutomatica")]
        [ResponseType(typeof(ResultadoReposicionAutomaticaDTO))]
        public async Task<IHttpActionResult> PostRellenarAutomatica(string origen, string destino,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (!ServicioCalendarioReposiciones.PuedeMantener(User))
            {
                return Prohibido(new UnauthorizedAccessException(
                    "Relanzar la reposición automática lo pueden hacer Almacén, Dirección e Informática."));
            }
            IServicioReposicionAutomatica servicio = automatica ?? new ServicioReposicionAutomatica(db);
            return Ok(await servicio.RellenarRuta(empresa, origen, destino, User).ConfigureAwait(false));
        }

        // GET api/Reposiciones/AvisosAutomatica?origen=ALC&empresa=1
        /// <summary>
        /// NestoAPI#622: lo que «Enviar reposición» dice arriba de lo que ha hecho hoy el job con las reposiciones de
        /// <paramref name="origen"/>: que la de hoy no se ha rellenado y por qué, o que se ha sumado a una que la tienda tenía
        /// sin enviar. Lista vacía si no hay nada que decir. Basta con estar identificado.
        /// </summary>
        [HttpGet]
        [Route("AvisosAutomatica")]
        [ResponseType(typeof(List<AvisoReposicionAutomaticaDTO>))]
        public async Task<IHttpActionResult> GetAvisosAutomatica(string origen, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            IServicioReposicionAutomatica servicio = automatica ?? new ServicioReposicionAutomatica(db);
            return Ok(await servicio.LeerAvisosDeHoy(empresa, origen).ConfigureAwait(false));
        }

        // GET api/Reposiciones/ProximaLlegada?origen=REI&destino=ALG&empresa=1
        /// <summary>
        /// NestoAPI#577: la próxima reposición de <paramref name="origen"/> a <paramref name="destino"/> según el calendario
        /// (ReposicionesCalendario), los festivos de los dos almacenes y la hora de corte del picking (parámetro
        /// «HoraCortePicking», 11:00 si no está): cuándo cierra, cuándo llega y qué día saldría el pedido que la espera.
        /// 404 si la ruta no tiene calendario activo.
        /// </summary>
        [HttpGet]
        [Route("ProximaLlegada")]
        [ResponseType(typeof(ProximaReposicionDTO))]
        public async Task<IHttpActionResult> GetProximaLlegada(string origen, string destino,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            ProximaReposicionDTO proxima = await Calendario.LeerProximaLlegada(empresa, origen, destino).ConfigureAwait(false);
            if (proxima == null)
            {
                return Content(HttpStatusCode.NotFound,
                    $"No hay calendario de reposiciones activo de {origen?.Trim().ToUpperInvariant()} a {destino?.Trim().ToUpperInvariant()}.");
            }
            return Ok(proxima);
        }

        // GET api/Reposiciones/Calendario?empresa=1
        /// <summary>NestoAPI#577: el calendario de reposiciones (todas las rutas, activas o no), por ruta, día y hora.</summary>
        [HttpGet]
        [Route("Calendario")]
        [ResponseType(typeof(List<ReposicionCalendarioDTO>))]
        public async Task<IHttpActionResult> GetCalendario(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return Ok(await Calendario.LeerCalendario(empresa).ConfigureAwait(false));
        }

        // GET api/Reposiciones/Calendario/PuedeEditar
        /// <summary>
        /// NestoAPI#577 (09/10/26): si el usuario actual puede cambiar el calendario (PUT Calendario). Son las mismas personas
        /// que pueden rellenar reposiciones a mano (parámetro «UsuariosRellenarReposicionManual» de la fila «(defecto)»; hoy
        /// Manuel, Alfredo y Carlos). Para que Nesto enseñe la ventana en solo lectura a los demás.
        /// </summary>
        [HttpGet]
        [Route("Calendario/PuedeEditar")]
        [ResponseType(typeof(bool))]
        public IHttpActionResult GetPuedeEditarCalendario()
        {
            return Ok(PermisoManual.Puede(User));
        }

        // PUT api/Reposiciones/Calendario   { Empresa?, Origen?, Destino?, Filas: [{ Id?, Origen, Destino, DiaSemana, HoraCierre, HoraLlegadaHabitual, LaborablesAntelacionCierre?, Activo }] }
        /// <summary>
        /// NestoAPI#577: guarda el calendario. Con Origen y Destino, Filas es la lista completa de esa ruta (lo que no venga se
        /// desactiva; nunca se borra); sin ellos, filas sueltas (crear o cambiar; una fila sin Id de un día que ya existe
        /// cambia esa). Devuelve el calendario entero. Solo quien está en «UsuariosRellenarReposicionManual» (403 si no; ver
        /// GET Calendario/PuedeEditar); 400 con el motivo si una fila no vale (almacenes, día 1-7, antelación 0-5, horas,
        /// día repetido en la ruta, antelaciones distintas en la misma ruta). Usuario (sin dominio) y FechaModificacion en
        /// cada fila que cambia. Cambiar la hora de cierre de un viaje que ya se ha rellenado no lo repite: vale desde el
        /// siguiente (ver ServicioReposicionAutomatica).
        /// </summary>
        [HttpPut]
        [Route("Calendario")]
        [ResponseType(typeof(List<ReposicionCalendarioDTO>))]
        public async Task<IHttpActionResult> PutCalendario([FromBody] GuardarCalendarioReposicionesDTO peticion)
        {
            if (!PermisoManual.Puede(User))
            {
                return Prohibido(new UnauthorizedAccessException(ServicioCalendarioReposiciones.MENSAJE_SIN_PERMISO));
            }
            if (peticion == null)
            {
                return BadRequest("Faltan las filas del calendario.");
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            return Ok(await Calendario.Guardar(peticion, usuario).ConfigureAwait(false));
        }

        // GET api/Reposiciones/EnTransito?empresa=1&almacen=ALC&productos=45146,45148
        /// <summary>
        /// Nesto#510: por producto, las unidades que viajan HACIA <paramref name="almacen"/> en una reposición: pendientes de
        /// recibir en su diario de entrada (con número de traspaso) o en una reposición en preparación hacia él (Traspaso null).
        /// Solo lectura. Lista vacía si no hay nada en camino.
        /// </summary>
        [HttpGet]
        [Route("EnTransito")]
        [ResponseType(typeof(List<ProductoEnTransitoDTO>))]
        public async Task<IHttpActionResult> GetEnTransito(string almacen, string productos = null,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            IServicioTransitoReposiciones servicio = transito ?? new ServicioTransitoReposiciones(db);
            return Ok(await servicio.LeerEnTransito(empresa, almacen, productos).ConfigureAwait(false));
        }

        // GET api/Reposiciones/Ruta?origen=ALG&destino=REI&empresa=1
        /// <summary>
        /// NestoAPI#600: los nombres de los dos almacenes (Almacenes.Descripción) y el título de la reposición, «De Algete
        /// a Reina», una sola vez: con ellos se titula la pantalla de crearla. 400 si los almacenes no valen para una
        /// reposición; 404 si alguno no existe.
        /// </summary>
        [HttpGet]
        [Route("Ruta")]
        [ResponseType(typeof(RutaReposicionDTO))]
        public async Task<IHttpActionResult> GetRuta(string origen, string destino, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            RutaReposicionDTO ruta = await preparacion.LeerRuta(empresa, origen, destino).ConfigureAwait(false);
            if (ruta == null)
            {
                return Content(HttpStatusCode.NotFound,
                    $"El almacén {origen?.Trim().ToUpperInvariant()} o el {destino?.Trim().ToUpperInvariant()} no existe en la empresa {empresa?.Trim()}.");
            }
            return Ok(ruta);
        }

        // GET api/Reposiciones/Propuesta?origen=ALG&destino=REI&empresa=1
        /// <summary>
        /// Qué habría que mandar de <paramref name="origen"/> a <paramref name="destino"/> (stock máximo, pendientes
        /// y stock de los dos almacenes), con prdRellenarReposicionStock2. 400 si los almacenes no valen. NestoAPI#577:
        /// una reposición anterior sin recibir ya no lo impide: lo que va hacia el destino cuenta como su stock y lo
        /// comprometido para salir del origen se le resta. Sin hora de corte (todos los pedidos pendientes).
        /// </summary>
        [HttpGet]
        [Route("Propuesta")]
        [ResponseType(typeof(List<LineaPropuestaReposicionDTO>))]
        public async Task<IHttpActionResult> GetPropuesta(string origen, string destino, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            List<LineaPropuestaReposicionDTO> propuesta = await new ServicioPropuestaReposicion(db)
                .CalcularPropuesta(empresa, origen, destino).ConfigureAwait(false);
            await CompletarFotos(propuesta).ConfigureAwait(false);
            return Ok(propuesta);
        }

        // POST api/Reposiciones   { Empresa, Origen, Destino, Fecha?, Lineas?: [{ Producto, Cantidad }] }
        /// <summary>
        /// Crea la reposición en el diario de salida del origen. Sin líneas, usa la propuesta (prdRellenarReposicionStock2).
        /// Desde una tienda la deja en preparación (NumTraspaso null, hasta Terminar). Desde Algete (control de ubicaciones)
        /// reserva los huecos y la cierra sin contabilizar: devuelve NumTraspaso y, por línea, Hueco / SinHueco; sale en
        /// GET api/Almacen/Recogidas (REPO). 409 si el origen tiene un inventario en curso o ya tiene una reposición en
        /// preparación; 403 si quien llama no puede (Algete: Almacén o Dirección). NestoAPI#577 (corte 3b): las rellena el
        /// job a la hora de cierre del calendario; a mano, solo quien esté en el parámetro «UsuariosRellenarReposicionManual»
        /// (403 con <see cref="PermisoRellenarReposicionManual.MENSAJE_SIN_PERMISO"/>; ver GET PuedeRellenarManual).
        /// </summary>
        [HttpPost]
        [Route("")]
        [ResponseType(typeof(ReposicionEnPreparacionDTO))]
        public async Task<IHttpActionResult> PostCrear([FromBody] CrearReposicionDTO peticion)
        {
            if (!PermisoManual.Puede(User))
            {
                return Prohibido(new UnauthorizedAccessException(PermisoRellenarReposicionManual.MENSAJE_SIN_PERMISO));
            }
            try
            {
                ReposicionEnPreparacionDTO creada = await ConFotos(await preparacion.Crear(peticion, User).ConfigureAwait(false)).ConfigureAwait(false);
                return Content(HttpStatusCode.Created, creada);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Prohibido(ex);
            }
        }

        // GET api/Reposiciones/EnPreparacion?origen=ALC&empresa=1
        /// <summary>La reposición que el origen tiene en preparación, con sus líneas. 404 si no hay ninguna.</summary>
        [HttpGet]
        [Route("EnPreparacion")]
        [ResponseType(typeof(ReposicionEnPreparacionDTO))]
        public async Task<IHttpActionResult> GetEnPreparacion(string origen, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            ReposicionEnPreparacionDTO reposicion = await ConFotos(await preparacion.LeerEnPreparacion(empresa, origen).ConfigureAwait(false)).ConfigureAwait(false);
            return reposicion == null ? (IHttpActionResult)NotFound() : Ok(reposicion);
        }

        // GET api/Reposiciones/EnPreparacion/Pdf?origen=ALC&empresa=1
        /// <summary>
        /// Sugerencia 564: la reposición en preparación en papel, para prepararla a mano (las tiendas, hasta tener Ariadna):
        /// ubicación si la hay, referencia, código de barras, descripción, cantidad y una casilla para marcar, en el orden del
        /// recorrido (por ubicación; sin ella, por familia y descripción). Sin las líneas a 0. 404 si no hay ninguna.
        /// </summary>
        [HttpGet]
        [Route("EnPreparacion/Pdf")]
        public async Task<HttpResponseMessage> GetEnPreparacionPdf(string origen, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            ListadoReposicionDTO datos = await Listado.LeerEnPreparacion(empresa, origen).ConfigureAwait(false);
            return datos == null
                ? Request.CreateErrorResponse(HttpStatusCode.NotFound, $"{origen?.Trim()} no tiene ninguna reposición en preparación.")
                : Pdf(datos);
        }

        // GET api/Reposiciones/Recepcion/80893/Pdf?almacen=ALG&empresa=1
        /// <summary>
        /// Sugerencia 564: lo que llega con una reposición pendiente de recibir en el almacén, en papel, para comprobarlo a
        /// mano. Mismo formato que la de preparar, con las ubicaciones del almacén que la recibe. 404 si no está pendiente ahí.
        /// </summary>
        [HttpGet]
        [Route("Recepcion/{numTraspaso:int}/Pdf")]
        public async Task<HttpResponseMessage> GetRecepcionPdf(int numTraspaso, string almacen, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            ListadoReposicionDTO datos = await Listado.LeerRecepcion(empresa, almacen, numTraspaso).ConfigureAwait(false);
            return datos == null
                ? Request.CreateErrorResponse(HttpStatusCode.NotFound, $"La reposición {numTraspaso} no está pendiente de recibir en {almacen?.Trim()}.")
                : Pdf(datos);
        }

        // GET api/Reposiciones/Enviadas?origen=ALC&empresa=1
        /// <summary>
        /// NestoAPI#685: las reposiciones que han salido de <paramref name="origen"/> y todavía no se han recibido en el destino,
        /// la más reciente primero, con cuántos bultos tienen. Con ellas Nesto «Enviar reposición» deja reimprimir los bultos
        /// (POST {n}/Bultos/Reimprimir) y el impreso (GET Enviada/{n}/Pdf) en cualquier momento. Lista vacía si no hay ninguna.
        /// </summary>
        [HttpGet]
        [Route("Enviadas")]
        [ResponseType(typeof(List<ReposicionEnviadaDTO>))]
        public async Task<IHttpActionResult> GetEnviadas(string origen, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (string.IsNullOrWhiteSpace(origen))
            {
                return BadRequest("Falta el almacén de origen.");
            }
            return Ok(await Listado.LeerEnviadas(empresa, origen).ConfigureAwait(false));
        }

        // GET api/Reposiciones/Enviada/81093/Pdf?origen=ALC&empresa=1
        /// <summary>
        /// NestoAPI#685: el impreso de una reposición ya enviada desde <paramref name="origen"/> y sin recibir, con el mismo
        /// formato que el de preparar (GET EnPreparacion/Pdf, el que sacan al enviarla) y las ubicaciones del origen. 404 si no
        /// es una enviada de ese origen pendiente de recibir.
        /// </summary>
        [HttpGet]
        [Route("Enviada/{numTraspaso:int}/Pdf")]
        public async Task<HttpResponseMessage> GetEnviadaPdf(int numTraspaso, string origen, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            ListadoReposicionDTO datos = await Listado.LeerEnviada(empresa, origen, numTraspaso).ConfigureAwait(false);
            return datos == null
                ? Request.CreateErrorResponse(HttpStatusCode.NotFound,
                    $"La reposición {numTraspaso} no es una reposición enviada desde {origen?.Trim()} pendiente de recibir.")
                : Pdf(datos);
        }

        private static HttpResponseMessage Pdf(ListadoReposicionDTO datos)
        {
            var respuesta = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new Infraestructure.Informes.GeneradorPdfReposicion().GenerarPdf(datos)
            };
            respuesta.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return respuesta;
        }

        // PUT api/Reposiciones/EnPreparacion/Lineas/561483500?origen=ALC&empresa=1   { Cantidad }
        /// <summary>Baja la cantidad de una línea (0 para no mandarla). 400 si se intenta subir; 404 si la línea no es de esa reposición.</summary>
        [HttpPut]
        [Route("EnPreparacion/Lineas/{numeroOrden:int}")]
        [ResponseType(typeof(ReposicionEnPreparacionDTO))]
        public async Task<IHttpActionResult> PutCantidad(int numeroOrden, [FromBody] CambiarCantidadReposicionDTO cambio, string origen,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (cambio == null)
            {
                return BadRequest("Falta la cantidad.");
            }
            try
            {
                return Ok(await ConFotos(await preparacion.CambiarCantidad(empresa, origen, numeroOrden, cambio.Cantidad, User).ConfigureAwait(false)).ConfigureAwait(false));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Prohibido(ex);
            }
        }

        // POST api/Reposiciones/EnPreparacion/Terminar?origen=ALC&empresa=1
        /// <summary>
        /// Da la reposición por preparada: le pone número de traspaso, contabiliza la salida del origen y deja la entrada
        /// pendiente de recibir en el destino (Almacen/Recepciones REPO). 409 si no hay ninguna en preparación.
        /// </summary>
        [HttpPost]
        [Route("EnPreparacion/Terminar")]
        [ResponseType(typeof(ResultadoTerminarReposicionDTO))]
        public async Task<IHttpActionResult> PostTerminar(string origen, [FromBody] TerminarReposicionDTO peticion,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            try
            {
                // Sugerencia 753: Ariadna manda lo dado por «No está»; sin cuerpo (Nesto), como siempre
                ResultadoTerminarReposicionDTO terminada = await preparacion.Terminar(empresa, origen, User, peticion?.NoEsta).ConfigureAwait(false);
                await CompletarFotos(terminada?.Lineas).ConfigureAwait(false);
                return Ok(terminada);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Prohibido(ex);
            }
        }

        // DELETE api/Reposiciones/80901?empresa=1
        /// <summary>
        /// Anula una reposición ya cerrada que aún no se ha recogido ni contabilizado (las de Algete creadas por el POST):
        /// devuelve las unidades a sus huecos y borra la salida y la entrada. 409 si ya tiene lecturas en Ariadna o está
        /// contabilizada; 404 si no hay ninguna por salir con ese número; 403 si no es de Almacén o Dirección.
        /// </summary>
        [HttpDelete]
        [Route("{numTraspaso:int}")]
        [ResponseType(typeof(ResultadoAnularReposicionDTO))]
        public async Task<IHttpActionResult> DeleteAnular(int numTraspaso, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            try
            {
                return Ok(await preparacion.Anular(empresa, numTraspaso, User).ConfigureAwait(false));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Prohibido(ex);
            }
        }

        // ---------------------------------------------------------------- NestoAPI#634: diferencias de reposición

        private readonly IServicioDiferenciasReposicion diferencias;

        private IServicioDiferenciasReposicion Diferencias => diferencias ?? (diferenciasPropias = diferenciasPropias
            ?? new ServicioDiferenciasReposicion(new RepositorioDiferenciasReposicionSql(db)));

        private ServicioDiferenciasReposicion diferenciasPropias;

        // GET api/Reposiciones/Diferencias?estado=Pendiente&almacen=ALG&empresa=1
        /// <summary>
        /// NestoAPI#634 (Nesto#526, Ariadna#25): las diferencias entre lo enviado y lo recibido en las reposiciones, con lo que
        /// hay que comprobar en el almacén de origen. <paramref name="estado"/>: Pendiente, Resuelta o Todas (por defecto,
        /// Pendiente). <paramref name="almacen"/>: el de origen (donde se comprueba). Almacén y Dirección ven todas; los demás,
        /// solo las de su almacén (AlmacénPedidoVta); otro almacén, 403. Pendientes primero, de la más antigua a la más nueva.
        /// </summary>
        [HttpGet]
        [Route("Diferencias")]
        [ResponseType(typeof(List<DiferenciaReposicionDTO>))]
        public async Task<IHttpActionResult> GetDiferencias(string estado = EstadosDiferenciaReposicion.PENDIENTE, string almacen = null,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            try
            {
                List<DiferenciaReposicionDTO> lista = await Diferencias.Listar(empresa, estado, almacen, User).ConfigureAwait(false);
                await CompletarFotosYReferencias(empresa, lista).ConfigureAwait(false);
                return Ok(lista);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Prohibido(ex);
            }
        }

        // GET api/Reposiciones/Diferencias/Contador?almacen=ALG&empresa=1
        /// <summary>NestoAPI#634: cuántas diferencias quedan por comprobar en <paramref name="almacen"/> (todas si no se dice y se puede).</summary>
        [HttpGet]
        [Route("Diferencias/Contador")]
        [ResponseType(typeof(ContadorDiferenciasReposicionDTO))]
        public async Task<IHttpActionResult> GetContadorDiferencias(string almacen = null, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            try
            {
                return Ok(await Diferencias.ContarPendientes(empresa, almacen, User).ConfigureAwait(false));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Prohibido(ex);
            }
        }

        // GET api/Reposiciones/Diferencias/12?empresa=1
        /// <summary>NestoAPI#634: una diferencia. 404 si no existe; 403 si no es de su almacén.</summary>
        [HttpGet]
        [Route("Diferencias/{id:int}")]
        [ResponseType(typeof(DiferenciaReposicionDTO))]
        public async Task<IHttpActionResult> GetDiferencia(int id, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            try
            {
                DiferenciaReposicionDTO diferencia = await Diferencias.Leer(empresa, id, User).ConfigureAwait(false);
                await CompletarFotosYReferencias(empresa, diferencia == null ? null : new[] { diferencia }).ConfigureAwait(false);
                return diferencia == null ? (IHttpActionResult)NotFound() : Ok(diferencia);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Prohibido(ex);
            }
        }

        // POST api/Reposiciones/Diferencias/12/Resolver?empresa=1   { "Resolucion": "Confirmada", "Comentario": "..." }
        /// <summary>
        /// NestoAPI#634: la comprobación en el origen. «Confirmada» (cuadra con lo que dejó el ajuste), «NoEstaba» (solo si se
        /// recibió de menos: lo que volvió al origen no está, baja), «Sobraba» (solo si se recibió de más: lo descontado sigue
        /// allí, alta) o «ErrorRecepcion» (se equivocó quien recibió: se deshace el ajuste del origen y se corrige el destino).
        /// Con el interruptor Reposiciones:AjustarStockDiferencias apagado solo se apunta (el stock no se toca y se dice).
        /// Resuelven Almacén y Dirección; las de una tienda, también su gente (403 si no). 404 si no existe; 409 si ya estaba
        /// resuelta; 400 si la resolución no existe o no vale para esa diferencia.
        /// </summary>
        [HttpPost]
        [Route("Diferencias/{id:int}/Resolver")]
        [ResponseType(typeof(ResultadoResolverDiferenciaReposicionDTO))]
        public async Task<IHttpActionResult> PostResolverDiferencia(int id, [FromBody] ResolverDiferenciaReposicionDTO peticion,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Resolucion))
            {
                return BadRequest("Falta la resolución (Confirmada, NoEstaba, Sobraba o ErrorRecepcion).");
            }
            try
            {
                ResultadoResolverDiferenciaReposicionDTO resuelta = await Diferencias.Resolver(empresa, id, peticion, User).ConfigureAwait(false);
                await CompletarFotosYReferencias(empresa, resuelta?.Diferencia == null ? null : new[] { resuelta.Diferencia }).ConfigureAwait(false);
                return Ok(resuelta);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Prohibido(ex);
            }
        }

        // ---------------------------------------------------------------- NestoAPI#645: etiquetas de bulto

        private readonly Infraestructure.PreparacionAlmacen.IServicioBultosReposicion bultos;

        private Infraestructure.PreparacionAlmacen.IServicioBultosReposicion Bultos => bultos
            ?? new Infraestructure.PreparacionAlmacen.ServicioBultosReposicion(new Infraestructure.PreparacionAlmacen.RepositorioBultosReposicionSql(db),
                new LectorParametrosUsuario(), Infraestructure.PreparacionAlmacen.ImpresoraEtiquetasConAjuste.EnWindows());

        // POST api/Reposiciones/81010/Bultos?empresa=1   { "Bultos": 3, "Imprimir": true }
        /// <summary>
        /// NestoAPI#645: al cerrar la reposición, cuántos bultos lleva (de 1 a 50) y una etiqueta por bulto («R81010-2», debajo
        /// «81010 · bulto 2/3 · ALC→ALG») en la impresora de etiquetas del usuario (ImpresoraCodBarras, la de las etiquetas de
        /// hueco). Si ya tenía bultos y no se ha leído ninguno, se rehacen con el nuevo total; si alguno se ha leído, 409.
        /// 404 si no existe el traspaso; 503 sin la tabla. Si la impresión falla, los bultos quedan creados y responde 502.
        /// </summary>
        [HttpPost]
        [Route("{numTraspaso:int}/Bultos")]
        [ResponseType(typeof(ResultadoBultosReposicionDTO))]
        public async Task<IHttpActionResult> PostBultos(int numTraspaso, [FromBody] CrearBultosReposicionDTO peticion,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return await ConErroresDeBultos(() => Bultos.Crear(EmpresaBultos(empresa), numTraspaso, peticion, User?.Identity?.Name)).ConfigureAwait(false);
        }

        // POST api/Reposiciones/81010/Bultos/Reimprimir?empresa=1
        /// <summary>NestoAPI#645: vuelve a imprimir las etiquetas de bulto que ya tiene. 404 si no tiene; 502 si la impresión falla.</summary>
        [HttpPost]
        [Route("{numTraspaso:int}/Bultos/Reimprimir")]
        [ResponseType(typeof(ResultadoBultosReposicionDTO))]
        public async Task<IHttpActionResult> PostReimprimirBultos(int numTraspaso, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return await ConErroresDeBultos(() => Bultos.Reimprimir(EmpresaBultos(empresa), numTraspaso, User?.Identity?.Name)).ConfigureAwait(false);
        }

        // GET api/Reposiciones/Bultos/R81010-2?empresa=1
        /// <summary>NestoAPI#645: de qué reposición es una etiqueta de bulto y si ya se ha leído al recibir. 404 si no es de ninguna.</summary>
        [HttpGet]
        [Route("Bultos/{codigo}")]
        [ResponseType(typeof(LecturaBultoReposicionDTO))]
        public async Task<IHttpActionResult> GetBulto(string codigo, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            try
            {
                LecturaBultoReposicionDTO bulto = await Bultos.Leer(EmpresaBultos(empresa), codigo).ConfigureAwait(false);
                return bulto == null
                    ? ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.NotFound, "Esa etiqueta no es de ninguna reposición."))
                    : (IHttpActionResult)Ok(bulto);
            }
            catch (Infraestructure.PreparacionAlmacen.BultosReposicionException ex)
            {
                return ResponseMessage(Request.CreateErrorResponse(ex.StatusCode, ex.Message));
            }
        }

        private async Task<IHttpActionResult> ConErroresDeBultos(Func<Task<ResultadoBultosReposicionDTO>> accion)
        {
            try
            {
                return Ok(await accion().ConfigureAwait(false));
            }
            catch (Infraestructure.PreparacionAlmacen.BultosReposicionException ex)
            {
                return ResponseMessage(Request.CreateErrorResponse(ex.StatusCode, ex.Message));
            }
            catch (Infraestructure.PreparacionAlmacen.ImpresionBultosException ex)
            {
                ElmahHelper.Log(ex);
                return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.BadGateway, ex.Message));
            }
        }

        private static string EmpresaBultos(string empresa)
            => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();

        /// <summary>Identificado pero sin permiso: 403, no 401 (con 401 el cliente intentaría refrescar el token).</summary>
        private IHttpActionResult Prohibido(UnauthorizedAccessException ex)
        {
            return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.Forbidden, ex.Message));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                (preparacion as IDisposable)?.Dispose();
                db?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
