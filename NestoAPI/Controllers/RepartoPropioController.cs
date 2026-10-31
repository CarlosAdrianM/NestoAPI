using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Infraestructure;
using NestoAPI.Infrastructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#739: el reparto propio desde Ariadna (lista del día, escanear un bulto, entregar con prueba de entrega,
    /// incidencias) y la prueba de entrega para Nesto. Contrato en el comentario de la issue del 29/10/26. Lo usan Almacén,
    /// Administración, Dirección e Informática; la prueba de entrega (firma, DNI) solo los grupos de
    /// <see cref="PermisosRepartoPropio.GRUPOS_DATOS_PERSONALES"/>.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/RepartoPropio")]
    public class RepartoPropioController : ApiController
    {
        private readonly NVEntities db;
        private readonly ServicioRepartoPropio servicio;

        public RepartoPropioController() : this(new NVEntities())
        {
        }

        internal RepartoPropioController(NVEntities db)
            : this(db, new ServicioRepartoPropio(new RepositorioRepartoPropio(db), avisarIncidencia: AvisadorIncidenciasEnvio.AvisarConContextoPropioAsync,
                informes: () => new Infraestructure.Informes.InformesService(db), carga: new RepositorioCargaFurgoneta(db),
                paradas: DependenciasParadasReparto.DeLaBaseDeDatos(db, new Infraestructure.Notificaciones.AvisosAlmacenSignalR())))
        {
        }

        // GET api/RepartoPropio/MisCobros?fecha=2026-10-29
        /// <summary>El extracto del día de la caja del usuario (su CajaDefecto). Sin caja, 400.</summary>
        [HttpGet]
        [Route("MisCobros")]
        [ResponseType(typeof(MisCobrosRepartoDTO))]
        public async Task<IHttpActionResult> GetMisCobros(DateTime? fecha = null)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido();
            }
            return Responder(await servicio.MisCobros(fecha, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI")));
        }

        internal RepartoPropioController(NVEntities db, ServicioRepartoPropio servicio)
        {
            this.db = db;
            this.servicio = servicio;
        }

        private ServicioCobrosReparto servicioCobros;

        /// <summary>NestoAPI#766: el cobro de deudas en efectivo (sustituible en tests).</summary>
        internal ServicioCobrosReparto ServicioCobros
        {
            get => servicioCobros ?? (servicioCobros = new ServicioCobrosReparto(new RepositorioCobrosReparto(db)));
            set => servicioCobros = value;
        }

        // POST api/RepartoPropio/Cobros { Cliente, Contacto, Efectos, Importe, ClaveIdempotencia, FechaHora }
        /// <summary>
        /// NestoAPI#766 (Ariadna#67): el repartidor cobra en EFECTIVO a un cliente algo que debía, como en la caja de Nesto: los
        /// efectos elegidos (o ninguno) y el importe; lo que sobra queda a cuenta. Contra su caja (CajaDefecto de su fila; sin ella,
        /// 400). Idempotente por ClaveIdempotencia (Ariadna lo manda por su cola sin cobertura).
        /// </summary>
        [HttpPost]
        [Route("Cobros")]
        [ResponseType(typeof(ResultadoCobroDeudaDTO))]
        public async Task<IHttpActionResult> Cobrar([FromBody] CobroDeudaRepartoDTO peticion)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido();
            }
            return Responder(await ServicioCobros.Cobrar(peticion, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI")));
        }

        // GET api/RepartoPropio/Envios?fecha=2026-10-29&soloMios=true
        /// <summary>
        /// Ariadna#63: con Repartidor y BultosCargados. NestoAPI#759: las paradas (envíos, notas y reposiciones) por Orden; con
        /// soloMios, las del usuario y las que no ha cargado nadie.
        /// </summary>
        [HttpGet]
        [Route("Envios")]
        [ResponseType(typeof(List<EnvioRepartoDTO>))]
        public async Task<IHttpActionResult> GetEnvios(DateTime? fecha = null, bool soloMios = false)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido();
            }
            return Responder(await servicio.Envios(fecha, soloMios, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI")));
        }

        // POST api/RepartoPropio/Cargar { Codigo, ClaveIdempotencia, FechaHora, Reasignar }
        /// <summary>
        /// Ariadna#63: el repartidor escanea un bulto al cargarlo en la furgoneta. 409 ENVIO_DE_OTRO_REPARTIDOR (error.details.repartidor)
        /// si lo lleva otro y no se pide Reasignar; 409 si el envío no está en reparto; 400 sin Scripts/Issue739_CargaFurgoneta.sql.
        /// </summary>
        [HttpPost]
        [Route("Cargar")]
        [ResponseType(typeof(ResultadoCargaBultoDTO))]
        public async Task<IHttpActionResult> Cargar([FromBody] CargaBultoRepartoDTO peticion)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido();
            }
            return Responder(await servicio.Cargar(peticion, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI"), Origen()));
        }

        // GET api/RepartoPropio/Furgoneta?usuario=&todos=false
        /// <summary>
        /// NestoAPI#772: lo que lleva encima el usuario (los bultos cuyo último movimiento es Cargado o Recogido suyo). Con
        /// <paramref name="usuario"/> (otro) o <paramref name="todos"/>, solo Almacén y Dirección. 400 sin Scripts/Issue772_FurgonetaBultos.sql.
        /// </summary>
        [HttpGet]
        [Route("Furgoneta")]
        [ResponseType(typeof(FurgonetaRepartoDTO))]
        public async Task<IHttpActionResult> GetFurgoneta(string usuario = null, bool todos = false)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido();
            }
            string yo = UsuarioAuditoriaHelper.Resolver(User, "NestoAPI");
            bool otro = !string.IsNullOrWhiteSpace(usuario) && !ServicioRepartoPropio.MismoUsuario(usuario, yo);
            if ((otro || todos) && !PermisosRepartoPropio.PuedeVerOtrasFurgonetas(User))
            {
                return Content(HttpStatusCode.Forbidden, new { Message = "Solo Almacén y Dirección pueden ver la furgoneta de otro repartidor." });
            }
            return Responder(await servicio.Furgoneta(otro ? usuario.Trim() : yo, todos));
        }

        // POST api/RepartoPropio/Recoger { Codigo, ClaveIdempotencia, FechaHora, Latitud, Longitud }
        /// <summary>
        /// NestoAPI#772: el repartidor lee un bulto que se trae (NR{pedido}-{N} de una nota de recogida o R{traspaso}-{N} de una
        /// reposición de una tienda a Algete) y pasa a su furgoneta. Con el último bulto de la reposición, queda «Recogida».
        /// </summary>
        [HttpPost]
        [Route("Recoger")]
        [ResponseType(typeof(ResultadoMovimientoBultoDTO))]
        public async Task<IHttpActionResult> Recoger([FromBody] MovimientoBultoRepartoDTO peticion)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido();
            }
            return Responder(await servicio.Recoger(peticion, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI"), Origen(), User));
        }

        // POST api/RepartoPropio/Descargar { Codigo, ClaveIdempotencia, FechaHora }
        /// <summary>NestoAPI#772: por la tarde, cada bulto que sale de la furgoneta. 409 NO_EN_FURGONETA si no está en ninguna.</summary>
        [HttpPost]
        [Route("Descargar")]
        [ResponseType(typeof(ResultadoMovimientoBultoDTO))]
        public async Task<IHttpActionResult> Descargar([FromBody] MovimientoBultoRepartoDTO peticion)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido();
            }
            return Responder(await servicio.Descargar(peticion, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI"), Origen()));
        }

        // POST api/RepartoPropio/Jornada/Terminar { ClaveIdempotencia, FechaHora, Confirmar }
        /// <summary>
        /// NestoAPI#772: terminar la jornada. Vacía, termina; con bultos y sin Confirmar, la lista (sin guardar nada); con Confirmar,
        /// queda apuntada y avisa a Almacén, Dirección y al repartidor en la campana de Nesto.
        /// </summary>
        [HttpPost]
        [Route("Jornada/Terminar")]
        [ResponseType(typeof(ResultadoJornadaRepartoDTO))]
        public async Task<IHttpActionResult> TerminarJornada([FromBody] TerminarJornadaRepartoDTO peticion)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido();
            }
            return Responder(await servicio.TerminarJornada(peticion, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI"), Origen()));
        }

        // GET api/RepartoPropio/SinCargar?fecha=2026-10-29
        /// <summary>Ariadna#63: los envíos Tramitados de ese día con algún bulto sin cargar.</summary>
        [HttpGet]
        [Route("SinCargar")]
        [ResponseType(typeof(List<EnvioSinCargarDTO>))]
        public async Task<IHttpActionResult> GetSinCargar(DateTime? fecha = null)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido();
            }
            return Responder(await servicio.SinCargar(fecha));
        }

        // GET api/RepartoPropio/Bulto/RP123456-2
        [HttpGet]
        [Route("Bulto/{codigo}")]
        [ResponseType(typeof(EnvioRepartoBultoDTO))]
        public async Task<IHttpActionResult> GetBulto(string codigo)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido();
            }
            return Responder(await servicio.Bulto(codigo));
        }

        // PUT api/RepartoPropio/Orden { FechaHora, Envios: [251300, -12, 251301] }
        /// <summary>
        /// NestoAPI#759: el orden de las paradas del repartidor (posición 1..n). Último gana por FechaHora; se ignoran las que no
        /// están en reparto, las de otro repartidor y las repetidas. 400 sin Scripts/Issue759_ParadasReparto.sql.
        /// </summary>
        [HttpPut]
        [Route("Orden")]
        [ResponseType(typeof(ResultadoOrdenRepartoDTO))]
        public async Task<IHttpActionResult> PutOrden([FromBody] OrdenRepartoDTO peticion)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido();
            }
            return Responder(await servicio.GuardarOrden(peticion, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI"), Origen()));
        }

        // POST api/RepartoPropio/OrdenSugerido { FechaHora, Fecha, Latitud, Longitud, DestinoLatitud, DestinoLongitud }
        /// <summary>
        /// NestoAPI#767 (Ariadna#68): reordena las paradas PENDIENTES del repartidor por el recorrido más corto desde su posición
        /// (Latitud/Longitud de la PDA; sin ellas, desde Algete) hasta Algete o el destino que mande (DestinoLatitud/DestinoLongitud), con OSRM o, si no responde en 5 s, con una
        /// heurística local. Lo guarda como PUT Orden (último gana; avisa «repartoCambiado» con Motivo «Orden»). 400 sin
        /// Scripts/Issue759_ParadasReparto.sql o con una posición no válida.
        /// </summary>
        [HttpPost]
        [Route("OrdenSugerido")]
        [ResponseType(typeof(OrdenSugeridoRepartoDTO))]
        public async Task<IHttpActionResult> PostOrdenSugerido([FromBody] PeticionOrdenSugeridoDTO peticion)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido();
            }
            return Responder(await servicio.OrdenSugerido(peticion, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI"), Origen()));
        }

        // POST api/RepartoPropio/Envios/123456/Entregar (las de reposición, con su EnvioId negativo: Envios/-12/Entregar)
        [HttpPost]
        [Route("Envios/{envioId:int}/Entregar")]
        [ResponseType(typeof(EstadoEnvioRepartoDTO))]
        public async Task<IHttpActionResult> Entregar(int envioId, [FromBody] EntregaRepartoDTO peticion)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido();
            }
            return Responder(await servicio.Entregar(envioId, peticion, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI"), Origen()));
        }

        // POST api/RepartoPropio/Envios/123456/Incidencia
        [HttpPost]
        [Route("Envios/{envioId:int}/Incidencia")]
        [ResponseType(typeof(EstadoEnvioRepartoDTO))]
        public async Task<IHttpActionResult> Incidencia(int envioId, [FromBody] IncidenciaRepartoDTO peticion)
        {
            if (!PermisosRepartoPropio.PuedeRepartir(User))
            {
                return Prohibido();
            }
            return Responder(await servicio.Incidencia(envioId, peticion, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI"), Origen()));
        }

        // GET api/RepartoPropio/Envios/123456/PruebaEntrega
        [HttpGet]
        [Route("Envios/{envioId:int}/PruebaEntrega")]
        [ResponseType(typeof(PruebaEntregaDTO))]
        public async Task<IHttpActionResult> GetPruebaEntrega(int envioId)
        {
            if (!PermisosRepartoPropio.PuedeVerDatosPersonales(User))
            {
                return Prohibido();
            }
            return Responder(await servicio.PruebaEntrega(envioId));
        }

        // GET api/RepartoPropio/Envios/123456/PruebaEntrega/Firma
        [HttpGet]
        [Route("Envios/{envioId:int}/PruebaEntrega/Firma")]
        public async Task<IHttpActionResult> GetFirma(int envioId)
        {
            if (!PermisosRepartoPropio.PuedeVerDatosPersonales(User))
            {
                return Prohibido();
            }
            ResultadoReparto<byte[]> resultado = await servicio.Firma(envioId);
            if (resultado.Tipo != TipoResultadoReparto.Ok)
            {
                return Responder(resultado);
            }
            var respuesta = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(resultado.Contenido) };
            respuesta.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            // Dato personal: que no se quede en ninguna caché.
            respuesta.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true, Private = true };
            return ResponseMessage(respuesta);
        }

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
                    if (resultado.Codigo != null)
                    {
                        // Ariadna#63: el formato de GlobalExceptionFilter ({ error: { code, message, details } }), y Message como siempre.
                        return Content(HttpStatusCode.Conflict, new
                        {
                            error = new { code = resultado.Codigo, message = resultado.Mensaje, details = resultado.Detalles },
                            Message = resultado.Mensaje
                        });
                    }
                    return Content(HttpStatusCode.Conflict, new { Message = resultado.Mensaje });
                default:
                    return Content(HttpStatusCode.BadRequest, new { Message = resultado.Mensaje });
            }
        }

        /// <summary>NestoAPI#759: la conexión de SignalR y el dispositivo de la PDA (para no avisarle de su propio cambio).</summary>
        private OrigenPeticionReparto Origen()
            => new OrigenPeticionReparto
            {
                Conexion = Cabecera(Infraestructure.Notificaciones.AvisosAlmacen.CABECERA_CONEXION),
                Dispositivo = Cabecera(Infraestructure.Notificaciones.AvisosAlmacen.CABECERA_DISPOSITIVO)
            };

        private string Cabecera(string nombre)
            => Request != null && Request.Headers.TryGetValues(nombre, out IEnumerable<string> valores)
                ? valores.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim()
                : null;

        private IHttpActionResult Prohibido()
            => Content(HttpStatusCode.Forbidden, new { Message = "Su usuario no tiene permiso para el reparto propio." });

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>NestoAPI#739: quién puede usar el reparto propio y quién ve la prueba de entrega (datos personales).</summary>
    public static class PermisosRepartoPropio
    {
        /// <summary>Los endpoints de reparto: lista, bulto, entregar, incidencia.</summary>
        public static readonly IReadOnlyList<string> GRUPOS_REPARTO = new[]
        {
            Constantes.GruposSeguridad.ALMACEN, Constantes.GruposSeguridad.ADMINISTRACION,
            Constantes.GruposSeguridad.DIRECCION, Constantes.GruposSeguridad.INFORMATICA
        };

        /// <summary>
        /// La prueba de entrega (firma y DNI): Almacén (y Logística, que hoy está dentro de Almacén: si llega a tener grupo
        /// propio, se añade AQUÍ), Administración y Dirección. Sin Informática: son datos personales (decisión del 29/10/26).
        /// </summary>
        public static readonly IReadOnlyList<string> GRUPOS_DATOS_PERSONALES = new[]
        {
            Constantes.GruposSeguridad.ALMACEN, Constantes.GruposSeguridad.ADMINISTRACION, Constantes.GruposSeguridad.DIRECCION
        };

        public static bool PuedeRepartir(System.Security.Principal.IPrincipal usuario) => EnAlguno(usuario, GRUPOS_REPARTO);

        public static bool PuedeVerDatosPersonales(System.Security.Principal.IPrincipal usuario) => EnAlguno(usuario, GRUPOS_DATOS_PERSONALES);

        /// <summary>NestoAPI#772: la furgoneta de otro repartidor (o la de todos): Almacén y Dirección.</summary>
        public static readonly IReadOnlyList<string> GRUPOS_OTRAS_FURGONETAS = new[]
        {
            Constantes.GruposSeguridad.ALMACEN, Constantes.GruposSeguridad.DIRECCION
        };

        public static bool PuedeVerOtrasFurgonetas(System.Security.Principal.IPrincipal usuario) => EnAlguno(usuario, GRUPOS_OTRAS_FURGONETAS);

        private static bool EnAlguno(System.Security.Principal.IPrincipal usuario, IEnumerable<string> grupos)
            => usuario != null && grupos.Any(g => usuario.IsInRoleSinDominio(g));
    }
}
