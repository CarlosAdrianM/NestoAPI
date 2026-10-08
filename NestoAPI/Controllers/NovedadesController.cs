using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Novedades;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Issue Nesto#372: changelog de novedades en lenguaje de usuario. Nesto lo consulta al
    /// arrancar tras una actualización y desde el menú Ayuda → Novedades. En el futuro lo
    /// podrán consumir también NestoApp y TiendasNuevaVision.
    /// </summary>
    public class NovedadesController : ApiController
    {
        private readonly IServicioNovedades servicio;
        // NestoAPI#520: votos y comentarios. null = sin feedback (las Novedades salen como siempre).
        private readonly IServicioFeedbackNovedades feedback;

        public NovedadesController() : this(new ServicioNovedades(), new ServicioFeedbackNovedades())
        {
            Notificaciones = new Infraestructure.Notificaciones.ServicioNotificacionesPush();
            LectorParametros = new LectorParametrosUsuario();
        }

        /// <summary>
        /// Carlos, 28/09/26: de dónde se lee a quién avisar de la actividad de Novedades. null = no se
        /// avisa a nadie (los tests que no lo necesitan).
        /// </summary>
        internal ILectorParametrosUsuario LectorParametros { get; set; }

        /// <summary>
        /// Nesto#477: con quién se avisa al autor cuando el asistente le contesta. null = no se avisa
        /// (los tests que no lo necesitan).
        /// </summary>
        internal Infraestructure.Notificaciones.IServicioNotificacionesPush Notificaciones { get; set; }

        public NovedadesController(IServicioNovedades servicio) : this(servicio, null) { }

        public NovedadesController(IServicioNovedades servicio, IServicioFeedbackNovedades feedback)
        {
            this.servicio = servicio;
            this.feedback = feedback;
        }

        // GET api/Novedades
        // GET api/Novedades?desdeVersion=1.10.5.3 (solo novedades de versiones POSTERIORES a la indicada)
        // GET api/Novedades?ambito=NestoApp (NestoAPI#489: solo las de ese producto; sin él, las del
        //     escritorio: Nesto y NestoAPI, que es lo que pide el Nesto publicado, que no manda ámbito)
        internal const string AMBITO_NESTOAPP = "NestoApp";
        /// <summary>NestoAPI#575: la app del almacén. Se pide siempre con ?ambito=Ariadna.</summary>
        internal const string AMBITO_ARIADNA = "Ariadna";

        internal static bool EsLlamadaDesdeNavegador(HttpRequestMessage request)
        {
            string userAgent = request?.Headers?.UserAgent?.ToString();
            return !string.IsNullOrEmpty(userAgent)
                && userAgent.IndexOf("Mozilla", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        [ResponseType(typeof(List<NovedadDTO>))]
        public IHttpActionResult GetNovedades(string desdeVersion = null, string ambito = null)
        {
            List<NovedadDTO> novedades = servicio.LeerNovedadesPublicadas();

            // NestoAPI#489: el ámbito se filtra ANTES que la versión. Nesto (1.10.x) y NestoApp (2.x)
            // tienen espacios de versiones distintos: comparar desdeVersion sin acotar el producto
            // descartaría o colaría entradas del otro.
            string ambitoEfectivo = AmbitoEfectivo(ambito);
            novedades = novedades.Where(n => EsDelAmbito(n.Ambito, ambitoEfectivo)).ToList();

            if (Version.TryParse(desdeVersion, out Version versionVista))
            {
                // Las entradas con versión no parseable no se filtran: mejor enseñarlas de más
                // que perder una novedad por un dato mal grabado.
                novedades = novedades
                    .Where(n => !Version.TryParse(n.Version, out Version v) || v > versionVista)
                    .ToList();
            }

            List<NovedadDTO> ordenadas = novedades
                .OrderByDescending(n => Version.TryParse(n.Version, out Version v) ? v : new Version(0, 0))
                .ThenBy(n => n.Id)
                .ToList();

            return Ok(OrdenarPorReacciones(ConFeedback(ordenadas)));
        }

        /// <summary>
        /// NestoAPI#535: dentro de cada versión, lo que más gusta arriba (👍 − 👎); a igualdad (o sin
        /// feedback), el orden de siempre. Las versiones no se mueven: de la más nueva a la más antigua.
        /// </summary>
        internal static List<NovedadDTO> OrdenarPorReacciones(List<NovedadDTO> novedades)
        {
            return novedades
                .Select((n, posicion) => new { Novedad = n, Posicion = posicion })
                .OrderByDescending(x => Version.TryParse(x.Novedad.Version, out Version v) ? v : new Version(0, 0))
                .ThenByDescending(x => x.Novedad is NovedadConFeedbackDTO f ? (f.VotosPositivos ?? 0) - (f.VotosNegativos ?? 0) : 0)
                .ThenBy(x => x.Posicion)
                .Select(x => x.Novedad)
                .ToList();
        }

        /// <summary>
        /// APAÑO TEMPORAL (23/09/26, NestoApp#186): la NestoApp publicada (2.20.5) pide sin ámbito y
        /// filtra en cliente, así que desde f167cd3e (sin ámbito = escritorio) no le llega ninguna
        /// novedad suya. Nesto llama con HttpClient de .NET (sin User-Agent de navegador); la app
        /// llama desde el WebView del móvil (User-Agent «Mozilla/...»). Se retira cuando todas las
        /// NestoApp en uso manden ?ambito=NestoApp.
        /// </summary>
        private string AmbitoEfectivo(string ambito) =>
            string.IsNullOrWhiteSpace(ambito) && EsLlamadaDesdeNavegador(Request) ? AMBITO_NESTOAPP : ambito;

        /// <summary>
        /// Con ámbito, solo las de ese producto. Sin ámbito (Nesto de escritorio, que solo manda
        /// desdeVersion=1.10.x): todo MENOS las de las apps (NestoApp y, desde NestoAPI#575, Ariadna).
        /// El 17/09/26 se colaron las 2.20.x de NestoApp en el popup de Nesto porque 2.20 > 1.10.
        /// </summary>
        internal static bool EsDelAmbito(string ambitoNovedad, string ambito)
        {
            return !string.IsNullOrWhiteSpace(ambito)
                ? string.Equals(ambitoNovedad?.Trim(), ambito.Trim(), StringComparison.OrdinalIgnoreCase)
                : !string.Equals(ambitoNovedad?.Trim(), AMBITO_NESTOAPP, StringComparison.OrdinalIgnoreCase)
                  && !string.Equals(ambitoNovedad?.Trim(), AMBITO_ARIADNA, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>El ámbito de lo que se crea desde cada programa.</summary>
        internal static string AmbitoDelCliente(string cliente)
        {
            if (cliente == ReglasFeedbackNovedades.CLIENTE_NESTOAPP)
            {
                return AMBITO_NESTOAPP;
            }
            return cliente == ReglasFeedbackNovedades.CLIENTE_ARIADNA ? AMBITO_ARIADNA : "Nesto";
        }

        #region NestoAPI#526/#527: sugerencias de los usuarios y buscador

        // GET api/Novedades/Sugerencias?ambito=NestoApp&incluirCerradas=false
        // Las que salen por delante de la versión actual: sin versión, abiertas, las más votadas arriba.
        [HttpGet]
        [Route("api/Novedades/Sugerencias")]
        [ResponseType(typeof(List<SugerenciaNovedadDTO>))]
        public IHttpActionResult GetSugerencias(string ambito = null, bool incluirCerradas = false)
        {
            string ambitoEfectivo = AmbitoEfectivo(ambito);
            // NestoAPI#575 (Ariadna) y quick win de Nesto/NestoApp: quien sugirió algo sigue viendo su
            // sugerencia descartada (y la respuesta de por qué) 30 días desde la última actividad.
            string usuario = incluirCerradas ? null : ReglasFeedbackNovedades.ClaveUsuario(User);
            bool descartadasDelAutor = usuario != null;
            DateTime ahora = DateTime.Now;
            List<SugerenciaNovedadDTO> sugerencias = servicio.LeerSugerencias(incluirCerradas || descartadasDelAutor)
                .Where(s => EsDelAmbito(s.Ambito, ambitoEfectivo))
                .Where(s => incluirCerradas || ReglasSugerenciasNovedades.SeVeEnLaLista(s, usuario, ahora))
                .Select(s => s.ADto())
                .ToList();
            OcultarContextoSiNoRevisa(sugerencias);
            _ = RellenarFeedback(sugerencias);
            return Ok(sugerencias
                .OrderByDescending(s => (s.VotosPositivos ?? 0) - (s.VotosNegativos ?? 0))
                .ThenByDescending(s => s.SugeridaFecha)
                .ToList());
        }

        // POST api/Novedades/Sugerencias  { "Texto": "Aquí iría bien un botón...", "ImagenBase64": "...", "VersionCliente": "1.10.31.0" }
        // NestoAPI#558: «Algo no funciona» es el mismo POST con "EsIncidencia": true (y, desde Nesto,
        // "Pantalla": "PlantillaVenta"): se guarda con categoría Incidencia y con el contexto automático.
        [HttpPost]
        [Authorize]
        [Route("api/Novedades/Sugerencias")]
        [ResponseType(typeof(SugerenciaNovedadDTO))]
        public async System.Threading.Tasks.Task<IHttpActionResult> PostSugerencia([FromBody] NuevaSugerenciaNovedadDTO sugerencia)
        {
            string usuario = ReglasFeedbackNovedades.ClaveUsuario(User);
            if (usuario == null)
            {
                return Unauthorized();
            }
            string cliente = ReglasFeedbackNovedades.Cliente(User);
            if (cliente == ReglasFeedbackNovedades.CLIENTE_TIENDA)
            {
                // Las sugerencias son de Nesto y de NestoApp; la tienda no tiene Novedades.
                return StatusCode(HttpStatusCode.Forbidden);
            }
            string error = ReglasFeedbackNovedades.Validar(sugerencia, out byte[] imagen, out string tipo);
            if (error != null)
            {
                return BadRequest(error);
            }
            var aGrabar = new SugerenciaNovedadAGrabar
            {
                Ambito = AmbitoDelCliente(cliente),
                Titulo = ReglasSugerenciasNovedades.TituloDesde(sugerencia.Texto),
                TextoOriginal = sugerencia.Texto.Trim(),
                Imagen = imagen,
                ImagenTipo = tipo,
                SugeridaPor = usuario,
                SugeridaNombre = ReglasFeedbackNovedades.NombreVisible(User),
                Categoria = sugerencia.EsIncidencia ? ReglasSugerenciasNovedades.CATEGORIA_INCIDENCIA : ReglasSugerenciasNovedades.CATEGORIA_SUGERENCIA,
                Contexto = sugerencia.EsIncidencia ? ContextoIncidencia(sugerencia) : null
            };
            int id = servicio.CrearSugerencia(aGrabar);
            // NestoAPI#537: también se puede mencionar a alguien al sugerir (el aviso lleva a la sugerencia)
            await AvisarMencionesEnSugerencia(id, aGrabar.TextoOriginal, aGrabar.SugeridaNombre, User?.Identity?.Name).ConfigureAwait(false);
            string tituloAviso = sugerencia.EsIncidencia
                ? $"{aGrabar.SugeridaNombre} ha avisado de algo que no funciona"
                : $"{aGrabar.SugeridaNombre} ha hecho una sugerencia";
            await AvisarActividad(id, 0, tituloAviso, aGrabar.TextoOriginal, aGrabar.TextoOriginal).ConfigureAwait(false);
            return Ok(new SugerenciaNovedadDTO
            {
                Id = id,
                Fecha = DateTime.Today,
                Categoria = aGrabar.Categoria,
                EsIncidencia = sugerencia.EsIncidencia,
                Titulo = aGrabar.Titulo,
                Ambito = aGrabar.Ambito,
                TextoOriginal = aGrabar.TextoOriginal,
                SugeridaNombre = aGrabar.SugeridaNombre,
                SugeridaFecha = DateTime.Now,
                Estado = ReglasSugerenciasNovedades.ESTADO_PENDIENTE,
                TieneImagen = imagen != null,
                VotosPositivos = 0,
                VotosNegativos = 0,
                NumeroComentarios = 0
            });
        }

        /// <summary>
        /// NestoAPI#558: versión, pantalla y errores de ELMAH del usuario de la última hora. Nunca rompe el
        /// aviso: si ELMAH no se puede leer, se guarda lo demás y el fallo va a ELMAH.
        /// </summary>
        internal string ContextoIncidencia(NuevaSugerenciaNovedadDTO incidencia)
        {
            List<ErrorElmahResumen> errores = null;
            try
            {
                List<string> usuarios = ReglasSugerenciasNovedades.UsuariosElmah(User?.Identity?.Name, DOMINIO);
                errores = servicio.LeerErroresElmah(usuarios,
                    DateTime.UtcNow.AddMinutes(-ReglasSugerenciasNovedades.MINUTOS_ERRORES_ELMAH),
                    ReglasSugerenciasNovedades.MAXIMO_ERRORES_ELMAH) ?? new List<ErrorElmahResumen>();
            }
            catch (Exception ex)
            {
                RegistrarSinRomper(new Exception("Novedades (NestoAPI#558): no se pudieron leer los errores de ELMAH para la incidencia. " + ex.Message, ex));
            }
            try
            {
                return ReglasSugerenciasNovedades.ComponerContexto(incidencia.VersionCliente, incidencia.Pantalla, errores);
            }
            catch (Exception ex)
            {
                RegistrarSinRomper(new Exception("Novedades (NestoAPI#558): no se pudo componer el contexto de la incidencia. " + ex.Message, ex));
                return null;
            }
        }

        private static void RegistrarSinRomper(Exception ex)
        {
            try
            {
                ElmahHelper.Log(ex);
            }
            catch
            {
                // El diagnóstico nunca rompe el aviso del usuario.
            }
        }

        /// <summary>
        /// NestoAPI#558: el contexto de las incidencias (errores de ELMAH de un usuario) solo lo ven
        /// Dirección / Informática.
        /// </summary>
        private void OcultarContextoSiNoRevisa(IEnumerable<SugerenciaNovedadDTO> sugerencias)
        {
            if (PuedeRevisarFeedback())
            {
                return;
            }
            foreach (SugerenciaNovedadDTO s in sugerencias)
            {
                s.Contexto = null;
            }
        }

        // GET api/Novedades/7/Imagen  -> la captura de la sugerencia con su content-type
        [HttpGet]
        [Authorize]
        [Route("api/Novedades/{id:int}/Imagen")]
        public HttpResponseMessage GetImagenNovedad(int id)
        {
            ImagenComentarioNovedad imagen = servicio.LeerImagen(id);
            if (imagen?.Imagen == null)
            {
                return Request.CreateResponse(HttpStatusCode.NotFound);
            }
            var respuesta = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(imagen.Imagen)
            };
            respuesta.Content.Headers.ContentType = new MediaTypeHeaderValue(imagen.ImagenTipo ?? ReglasFeedbackNovedades.TIPO_PNG);
            return respuesta;
        }

        // PUT api/Novedades/Sugerencias/7  { "Titulo": "...", "Descripcion": "...", "Estado": "Aceptada", "Version": "1.10.31.0" }
        // Desarrollo (Dirección / Informática): el texto claro, el estado y, al implementarla, la versión.
        [HttpPut]
        [Authorize]
        [Route("api/Novedades/Sugerencias/{id:int}")]
        public IHttpActionResult PutSugerencia(int id, [FromBody] ActualizarSugerenciaNovedadDTO cambios)
        {
            if (!PuedeRevisarFeedback())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            string error = ReglasSugerenciasNovedades.Normalizar(cambios);
            if (error != null)
            {
                return BadRequest(error);
            }
            return servicio.ActualizarSugerencia(id, cambios, User?.Identity?.Name)
                ? (IHttpActionResult)StatusCode(HttpStatusCode.NoContent)
                : NotFound();
        }

        // GET api/Novedades/Buscar?texto=reembolso envío&ambito=NestoApp
        // Novedades y sugerencias que contienen todas las palabras, sin distinguir tildes. Cada una
        // dice su versión (null = sugerencia) para que el cliente salte a ella y se pueda comentar.
        [HttpGet]
        [Route("api/Novedades/Buscar")]
        [ResponseType(typeof(List<SugerenciaNovedadDTO>))]
        public IHttpActionResult GetBuscar(string texto, string ambito = null)
        {
            List<string> palabras = ReglasSugerenciasNovedades.Palabras(texto);
            if (palabras.Count == 0)
            {
                return BadRequest($"Escribe al menos una palabra de {ReglasSugerenciasNovedades.LONGITUD_MINIMA_PALABRA} letras");
            }
            string ambitoEfectivo = AmbitoEfectivo(ambito);
            List<SugerenciaNovedadDTO> encontradas = servicio.Buscar(palabras)
                .Where(n => EsDelAmbito(n.Ambito, ambitoEfectivo))
                .Select(n => n.ADto())
                .ToList();
            OcultarContextoSiNoRevisa(encontradas);
            _ = RellenarFeedback(encontradas);
            return Ok(encontradas);
        }

        #endregion

        #region NestoAPI#520: feedback de los usuarios (votos y comentarios)

        // Quién puede leer el feedback de todos para el desarrollo (revisión diaria, como ELMAH).
        internal const string GRUPO_INFORMATICA = "Informatica";
        private static int feedbackFalloRegistrado;

        /// <summary>
        /// Añade a cada novedad sus votos, el voto de quien pregunta y el nº de comentarios, con UNA
        /// consulta para todas (nada de una por novedad). Si el feedback falla (p. ej. tablas aún no
        /// creadas), las Novedades salen igual y el fallo se registra en ELMAH una sola vez.
        /// </summary>
        internal List<NovedadDTO> ConFeedback(List<NovedadDTO> novedades)
        {
            if (feedback == null || novedades.Count == 0)
            {
                return novedades;
            }
            List<NovedadConFeedbackDTO> conFeedback = novedades.Select(NovedadConFeedbackDTO.Desde).ToList();
            return RellenarFeedback(conFeedback) ? conFeedback.Cast<NovedadDTO>().ToList() : novedades;
        }

        /// <summary>
        /// Rellena en su sitio votos, voto propio y nº de comentarios. False si no se ha podido (sin
        /// feedback o fallo de las tablas): quien llama sirve las novedades sin él.
        /// </summary>
        internal bool RellenarFeedback<T>(List<T> novedades) where T : NovedadConFeedbackDTO
        {
            if (feedback == null || novedades.Count == 0)
            {
                return false;
            }
            List<ResumenFeedbackNovedad> resumen;
            try
            {
                resumen = feedback.LeerResumen(novedades.Select(n => n.Id), ReglasFeedbackNovedades.ClaveUsuario(User))
                    ?? new List<ResumenFeedbackNovedad>();
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref feedbackFalloRegistrado, 1) == 0)
                {
                    try
                    {
                        ElmahHelper.Log(new Exception("Novedades (#520): no se pudo leer el feedback; se sirven sin votos ni comentarios. " + ex.Message, ex));
                    }
                    catch
                    {
                        // El diagnóstico nunca rompe las Novedades.
                    }
                }
                return false;
            }
            Dictionary<int, ResumenFeedbackNovedad> porNovedad = resumen.GroupBy(r => r.NovedadId).ToDictionary(g => g.Key, g => g.First());
            foreach (T novedad in novedades)
            {
                porNovedad.TryGetValue(novedad.Id, out ResumenFeedbackNovedad r);
                novedad.VotosPositivos = r?.Positivos ?? 0;
                novedad.VotosNegativos = r?.Negativos ?? 0;
                novedad.MiVoto = r?.MiVoto;
                novedad.NumeroComentarios = r?.Comentarios ?? 0;
            }
            return true;
        }

        // PUT api/Novedades/5/Voto  { "Voto": 1 | -1 | 0 }   (0 = quitar el voto)
        [HttpPut]
        [Authorize]
        [Route("api/Novedades/{id:int}/Voto")]
        public async System.Threading.Tasks.Task<IHttpActionResult> PutVoto(int id, [FromBody] VotoNovedadDTO voto)
        {
            string usuario = ReglasFeedbackNovedades.ClaveUsuario(User);
            if (usuario == null)
            {
                return Unauthorized();
            }
            if (voto == null || !ReglasFeedbackNovedades.EsVotoValido(voto.Voto))
            {
                return BadRequest("El voto debe ser 1 (me gusta), -1 (no me gusta) o 0 (quitar el voto).");
            }
            if (!feedback.ExisteNovedad(id))
            {
                return NotFound();
            }
            feedback.Votar(id, usuario, ReglasFeedbackNovedades.Cliente(User), voto.Voto);
            if (voto.Voto != 0)
            {
                string titulo = feedback.LeerTituloNovedad(id);
                await AvisarActividad(id, 0,
                    $"{ReglasFeedbackNovedades.NombreVisible(User)} ha votado {(voto.Voto > 0 ? "👍" : "👎")} en Novedades",
                    string.IsNullOrWhiteSpace(titulo) ? $"Novedad {id}" : titulo, null).ConfigureAwait(false);
            }
            return StatusCode(HttpStatusCode.NoContent);
        }

        // GET api/Novedades/5/Comentarios
        [HttpGet]
        [Authorize]
        [Route("api/Novedades/{id:int}/Comentarios")]
        [ResponseType(typeof(List<ComentarioNovedadDTO>))]
        public IHttpActionResult GetComentarios(int id)
        {
            return Ok(feedback.LeerComentarios(id, ReglasFeedbackNovedades.ClaveUsuario(User)));
        }

        // POST api/Novedades/5/Comentarios  { "Texto": "...", "ImagenBase64": "...", "VersionCliente": "1.10.29.1" }
        [HttpPost]
        [Authorize]
        [Route("api/Novedades/{id:int}/Comentarios")]
        [ResponseType(typeof(ComentarioNovedadDTO))]
        public async System.Threading.Tasks.Task<IHttpActionResult> PostComentario(int id, [FromBody] NuevoComentarioNovedadDTO comentario)
        {
            string usuario = ReglasFeedbackNovedades.ClaveUsuario(User);
            if (usuario == null)
            {
                return Unauthorized();
            }
            string error = ReglasFeedbackNovedades.Validar(comentario, out byte[] imagen, out string tipo);
            if (error != null)
            {
                return BadRequest(error);
            }
            if (!feedback.ExisteNovedad(id))
            {
                return NotFound();
            }
            var aGrabar = new ComentarioNovedadAGrabar
            {
                NovedadId = id,
                Usuario = usuario,
                NombreVisible = ReglasFeedbackNovedades.NombreVisible(User),
                Cliente = ReglasFeedbackNovedades.Cliente(User),
                VersionCliente = string.IsNullOrWhiteSpace(comentario.VersionCliente) ? null
                    : comentario.VersionCliente.Trim().Substring(0, Math.Min(30, comentario.VersionCliente.Trim().Length)),
                Texto = comentario.Texto.Trim(),
                Imagen = imagen,
                ImagenTipo = tipo
            };
            int nuevoId = feedback.CrearComentario(aGrabar);
            // NestoAPI#537: a quien se mencione con @ le llega el aviso
            await AvisarMenciones(id, nuevoId, aGrabar.Texto, aGrabar.NombreVisible, User?.Identity?.Name, null).ConfigureAwait(false);
            string tituloComentado = feedback.LeerTituloNovedad(id);
            await AvisarActividad(id, nuevoId,
                string.IsNullOrWhiteSpace(tituloComentado) ? $"{aGrabar.NombreVisible} ha comentado en Novedades"
                    : $"{aGrabar.NombreVisible} ha comentado «{tituloComentado}»",
                aGrabar.Texto, aGrabar.Texto).ConfigureAwait(false);
            return Ok(new ComentarioNovedadDTO
            {
                Id = nuevoId,
                NovedadId = id,
                NombreVisible = aGrabar.NombreVisible,
                Cliente = aGrabar.Cliente,
                VersionCliente = aGrabar.VersionCliente,
                Texto = aGrabar.Texto,
                Fecha = DateTime.Now,
                TieneImagen = imagen != null,
                EsMio = true
            });
        }

        // POST api/Novedades/5/Comentarios/Asistente  { "Texto": "...", "ComentariosContestados": [1] }
        // NestoAPI#531: el asistente IA contesta como él mismo (solo Dirección / Informática, que
        // es desde donde se lanza). Lo contestado queda revisado.
        [HttpPost]
        [Authorize]
        [Route("api/Novedades/{id:int}/Comentarios/Asistente")]
        [ResponseType(typeof(ComentarioNovedadDTO))]
        public async System.Threading.Tasks.Task<IHttpActionResult> PostComentarioAsistente(int id, [FromBody] NuevoComentarioAsistenteDTO comentario)
        {
            if (!PuedeRevisarFeedback())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            string error = ReglasFeedbackNovedades.Validar(comentario, out byte[] imagen, out string tipo);
            if (error != null)
            {
                return BadRequest(error);
            }
            if (!feedback.ExisteNovedad(id))
            {
                return NotFound();
            }
            var aGrabar = new ComentarioNovedadAGrabar
            {
                NovedadId = id,
                Usuario = ReglasFeedbackNovedades.USUARIO_ASISTENTE,
                NombreVisible = ReglasFeedbackNovedades.NOMBRE_ASISTENTE,
                Cliente = ReglasFeedbackNovedades.CLIENTE_ASISTENTE,
                Texto = comentario.Texto.Trim(),
                Imagen = imagen,
                ImagenTipo = tipo
            };
            int nuevoId = feedback.CrearComentario(aGrabar);
            List<int> contestados = (comentario.ComentariosContestados ?? new List<int>()).Distinct().ToList();
            foreach (int contestado in contestados)
            {
                _ = feedback.MarcarRevisado(contestado);
            }
            List<string> avisados = await AvisarALosContestados(id, nuevoId, aGrabar.Texto, contestados).ConfigureAwait(false);
            await AvisarMenciones(id, nuevoId, aGrabar.Texto, aGrabar.NombreVisible, ReglasFeedbackNovedades.USUARIO_ASISTENTE, avisados).ConfigureAwait(false);
            await AvisarAlSupervisorDeLaRespuestaDelAsistente(id, nuevoId, aGrabar.Texto, avisados).ConfigureAwait(false);
            return Ok(new ComentarioNovedadDTO
            {
                Id = nuevoId,
                NovedadId = id,
                NombreVisible = aGrabar.NombreVisible,
                Cliente = aGrabar.Cliente,
                Texto = aGrabar.Texto,
                Fecha = DateTime.Now,
                TieneImagen = imagen != null,
                EsMio = false
            });
        }

        internal const string TIPO_NOTIFICACION_RESPUESTA = "NovedadComentario";

        internal const string SUPERVISOR_ACTIVIDAD_POR_DEFECTO = "Carlos";
        private const string DOMINIO = "NUEVAVISION\\";

        /// <summary>
        /// Carlos, 28/09/26: a quién avisar de la actividad de los usuarios (parámetro
        /// AvisarActividadNovedadesA de «(defecto)»; sin fila, Carlos; "0" o vacío, a nadie).
        /// </summary>
        internal string SupervisorActividad()
        {
            if (LectorParametros == null)
            {
                return null;
            }
            string valor = LectorParametros.LeerParametro(Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO, Constantes.ParametrosUsuario.AVISAR_ACTIVIDAD_NOVEDADES_A);
            if (valor == null)
            {
                return SUPERVISOR_ACTIVIDAD_POR_DEFECTO;
            }
            valor = SinDominio(valor);
            return valor.Length == 0 || valor == "0" ? null : valor;
        }

        private static string SinDominio(string usuario) => usuario.Substring(usuario.IndexOf('\\') + 1).Trim();

        /// <summary>
        /// Carlos, 28/09/26: cada comentario, voto o sugerencia de un usuario le llega al supervisor
        /// (Carlos) por el programa donde se ha hecho: desde Nesto, a su campana (buzón + SignalR); desde
        /// NestoApp, push a su móvil. No se avisa de lo que hace él mismo, ni si ya le llega como @mención
        /// en el mismo texto. Nunca rompe la acción del usuario.
        /// </summary>
        private async System.Threading.Tasks.Task AvisarActividad(int novedadId, int comentarioId, string titulo, string cuerpo, string textoConMenciones)
        {
            if (Notificaciones == null)
            {
                return;
            }
            try
            {
                string supervisor = SupervisorActividad();
                if (supervisor == null || EsElMismoUsuario(User?.Identity?.Name, supervisor))
                {
                    return;
                }
                if (!string.IsNullOrEmpty(textoConMenciones) && ReglasMenciones.Resolver(ReglasMenciones.Extraer(textoConMenciones),
                        new List<MencionableDTO> { new MencionableDTO { Nombre = supervisor, Clave = supervisor } }, null).Any())
                {
                    return;
                }
                cuerpo = cuerpo ?? string.Empty;
                var notificacion = new NotificacionPushDTO
                {
                    Titulo = titulo,
                    Cuerpo = cuerpo.Length > 200 ? cuerpo.Substring(0, 197) + "…" : cuerpo,
                    Tipo = TIPO_NOTIFICACION_RESPUESTA,
                    Datos = new Dictionary<string, string>
                    {
                        ["tipo"] = TIPO_NOTIFICACION_RESPUESTA,
                        ["novedadId"] = novedadId.ToString()
                    }
                };
                if (comentarioId > 0)
                {
                    notificacion.Datos["comentarioId"] = comentarioId.ToString();
                }
                if (ReglasFeedbackNovedades.Cliente(User) == ReglasFeedbackNovedades.CLIENTE_NESTOAPP)
                {
                    _ = await Notificaciones.EnviarAUsuario(supervisor, Constantes.Aplicaciones.NESTO_APP, notificacion).ConfigureAwait(false);
                }
                else
                {
                    await Notificaciones.GuardarEnBuzonDeUsuario(DOMINIO + supervisor, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("Novedades: no se pudo avisar de la actividad al supervisor. " + ex.Message, ex));
            }
        }

        /// <summary>
        /// Carlos, 30/09/26: el asistente contestó a Laura y a Enrique y al supervisor no le llegó
        /// nada (AvisarActividad no avisa de lo que hace uno mismo, y el asistente contesta con su
        /// usuario). Lo que dice el asistente a los usuarios le llega también a su campana de Nesto,
        /// salvo que ya le haya llegado como autor contestado o como @mención. Nunca rompe la respuesta.
        /// </summary>
        private async System.Threading.Tasks.Task AvisarAlSupervisorDeLaRespuestaDelAsistente(int novedadId, int comentarioId, string texto, List<string> yaAvisados)
        {
            if (Notificaciones == null)
            {
                return;
            }
            try
            {
                string supervisor = SupervisorActividad();
                if (supervisor == null
                    || (yaAvisados ?? new List<string>()).Any(a => EsElMismoUsuario(a, supervisor))
                    || ReglasMenciones.Resolver(ReglasMenciones.Extraer(texto),
                        new List<MencionableDTO> { new MencionableDTO { Nombre = supervisor, Clave = supervisor } }, null).Any())
                {
                    return;
                }
                await Notificaciones.GuardarEnBuzonDeUsuario(DOMINIO + supervisor, Constantes.Aplicaciones.NESTO, new NotificacionPushDTO
                {
                    Titulo = ReglasFeedbackNovedades.NOMBRE_ASISTENTE + " ha contestado en Novedades",
                    Cuerpo = texto.Length > 200 ? texto.Substring(0, 197) + "…" : texto,
                    Tipo = TIPO_NOTIFICACION_RESPUESTA,
                    Datos = new Dictionary<string, string>
                    {
                        ["tipo"] = TIPO_NOTIFICACION_RESPUESTA,
                        ["novedadId"] = novedadId.ToString(),
                        ["comentarioId"] = comentarioId.ToString()
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("Novedades: no se pudo avisar al supervisor de la respuesta del asistente. " + ex.Message, ex));
            }
        }

        internal static bool EsElMismoUsuario(string nombreIdentidad, string usuario)
            => !string.IsNullOrWhiteSpace(nombreIdentidad)
               && string.Equals(SinDominio(nombreIdentidad), usuario, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Nesto#477: quien comentó se entera de que le hemos contestado. En Nesto (sin push) queda en
        /// su buzón, la campana de la barra; en NestoApp, push + buzón. Al pulsarla se abre la novedad
        /// en el comentario (Datos: novedadId, comentarioId). Nunca rompe la respuesta.
        /// </summary>
        private async System.Threading.Tasks.Task<List<string>> AvisarALosContestados(int novedadId, int respuestaId, string texto, List<int> contestados)
        {
            var avisados = new List<string>();
            if (Notificaciones == null || contestados.Count == 0)
            {
                return avisados;
            }
            try
            {
                var notificacion = new NotificacionPushDTO
                {
                    Titulo = "Te han contestado en Novedades",
                    Cuerpo = ReglasFeedbackNovedades.NOMBRE_ASISTENTE + ": " + (texto.Length > 200 ? texto.Substring(0, 197) + "…" : texto),
                    Tipo = TIPO_NOTIFICACION_RESPUESTA,
                    Datos = new Dictionary<string, string>
                    {
                        ["tipo"] = TIPO_NOTIFICACION_RESPUESTA,
                        ["novedadId"] = novedadId.ToString(),
                        ["comentarioId"] = respuestaId.ToString()
                    }
                };
                IEnumerable<AutorComentarioNovedad> autores = feedback.LeerAutores(contestados)
                    .Where(a => !string.Equals(a.Usuario?.Trim(), ReglasFeedbackNovedades.USUARIO_ASISTENTE, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(a => a.Usuario?.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First());
                foreach (AutorComentarioNovedad autor in autores)
                {
                    if (autor.Cliente == ReglasFeedbackNovedades.CLIENTE_NESTOAPP)
                    {
                        // En la app los dispositivos van por el UserName, que es el nombre visible
                        _ = await Notificaciones.EnviarAUsuario(autor.NombreVisible, Constantes.Aplicaciones.NESTO_APP, notificacion).ConfigureAwait(false);
                        avisados.Add(autor.NombreVisible);
                    }
                    else if (autor.Cliente == ReglasFeedbackNovedades.CLIENTE_NESTO)
                    {
                        await Notificaciones.GuardarEnBuzonDeUsuario(autor.Usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                        avisados.Add(autor.Usuario);
                    }
                    else if (autor.Cliente == ReglasFeedbackNovedades.CLIENTE_ARIADNA)
                    {
                        // NestoAPI#575: a la campana de Ariadna (sin push). El buzón va por el UserName.
                        await Notificaciones.GuardarEnBuzonDeUsuario(autor.NombreVisible, Constantes.Aplicaciones.ARIADNA, notificacion).ConfigureAwait(false);
                        avisados.Add(autor.NombreVisible);
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("Novedades (Nesto#477): no se pudo avisar de la respuesta al autor. " + ex.Message, ex));
            }
            return avisados;
        }

        // GET api/Novedades/Mencionables?ambito=NestoApp  → para el autocompletado al escribir @
        [HttpGet]
        [Authorize]
        [Route("api/Novedades/Mencionables")]
        [ResponseType(typeof(List<MencionableDTO>))]
        public IHttpActionResult GetMencionables(string ambito = null)
        {
            if (feedback == null)
            {
                return Ok(new List<MencionableDTO>());
            }
            return Ok(feedback.LeerMencionables(EsDeNestoApp(AmbitoEfectivo(ambito))));
        }

        /// <summary>NestoAPI#537: la mención en una sugerencia lleva a la sugerencia (sin comentario).</summary>
        private System.Threading.Tasks.Task AvisarMencionesEnSugerencia(int novedadId, string texto, string nombreAutor, string claveAutor)
        {
            return AvisarMenciones(novedadId, 0, texto, nombreAutor, claveAutor, null);
        }

        private static bool EsDeNestoApp(string ambito) =>
            string.Equals(ambito?.Trim(), AMBITO_NESTOAPP, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// NestoAPI#537: a cada @mencionado le llega «X te ha mencionado en Novedades», con el mismo
        /// salto al comentario que la respuesta. Se busca entre los usuarios del ámbito de la novedad
        /// (Nesto: buzón; NestoApp: push + buzón). Ni a quien escribe ni a quien ya se ha avisado por
        /// otra vía (el autor contestado). Nunca rompe el comentario.
        /// </summary>
        private async System.Threading.Tasks.Task AvisarMenciones(int novedadId, int comentarioId, string texto, string nombreAutor,
            string claveAutor, IEnumerable<string> yaAvisados)
        {
            List<string> menciones = ReglasMenciones.Extraer(texto);
            if (Notificaciones == null || feedback == null || menciones.Count == 0)
            {
                return;
            }
            try
            {
                bool deNestoApp = EsDeNestoApp(feedback.LeerAmbitoNovedad(novedadId));
                List<string> excluidos = (yaAvisados ?? Enumerable.Empty<string>()).Where(x => x != null).ToList();
                IEnumerable<MencionableDTO> mencionados = ReglasMenciones.Resolver(menciones, feedback.LeerMencionables(deNestoApp), claveAutor)
                    .Where(m => !excluidos.Contains(m.Clave, StringComparer.OrdinalIgnoreCase));
                var notificacion = new NotificacionPushDTO
                {
                    Titulo = $"{nombreAutor} te ha mencionado en Novedades",
                    Cuerpo = texto.Length > 200 ? texto.Substring(0, 197) + "…" : texto,
                    Tipo = TIPO_NOTIFICACION_RESPUESTA,
                    Datos = new Dictionary<string, string>
                    {
                        ["tipo"] = TIPO_NOTIFICACION_RESPUESTA,
                        ["novedadId"] = novedadId.ToString()
                    }
                };
                if (comentarioId > 0)
                {
                    // Sin comentario (mención al sugerir): el aviso lleva a la sugerencia
                    notificacion.Datos["comentarioId"] = comentarioId.ToString();
                }
                foreach (MencionableDTO mencionado in mencionados)
                {
                    if (mencionado.Aplicacion == Constantes.Aplicaciones.NESTO_APP)
                    {
                        _ = await Notificaciones.EnviarAUsuario(mencionado.Clave, Constantes.Aplicaciones.NESTO_APP, notificacion).ConfigureAwait(false);
                    }
                    else
                    {
                        await Notificaciones.GuardarEnBuzonDeUsuario(mencionado.Clave, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("Novedades (NestoAPI#537): no se pudo avisar a los mencionados. " + ex.Message, ex));
            }
        }

        // GET api/Novedades/Comentarios/7/Imagen  → la captura con su content-type
        [HttpGet]
        [Authorize]
        [Route("api/Novedades/Comentarios/{id:int}/Imagen")]
        public HttpResponseMessage GetImagenComentario(int id)
        {
            ImagenComentarioNovedad imagen = feedback.LeerImagen(id);
            if (imagen?.Imagen == null)
            {
                return Request.CreateResponse(HttpStatusCode.NotFound);
            }
            var respuesta = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(imagen.Imagen)
            };
            respuesta.Content.Headers.ContentType = new MediaTypeHeaderValue(imagen.ImagenTipo ?? ReglasFeedbackNovedades.TIPO_PNG);
            return respuesta;
        }

        // DELETE api/Novedades/Comentarios/7  → solo su autor
        [HttpDelete]
        [Authorize]
        [Route("api/Novedades/Comentarios/{id:int}")]
        public IHttpActionResult DeleteComentario(int id)
        {
            string usuario = ReglasFeedbackNovedades.ClaveUsuario(User);
            if (usuario == null)
            {
                return Unauthorized();
            }
            string autor = feedback.LeerAutorComentario(id);
            if (autor == null)
            {
                return NotFound();
            }
            if (!string.Equals(autor.Trim(), usuario, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            feedback.BorrarComentario(id);
            return StatusCode(HttpStatusCode.NoContent);
        }

        // GET api/Novedades/Feedback?desde=2026-09-22&soloNoRevisados=true  (desarrollo: Dirección / Informática)
        [HttpGet]
        [Authorize]
        [Route("api/Novedades/Feedback")]
        [ResponseType(typeof(FeedbackNovedadesDTO))]
        public IHttpActionResult GetFeedback(DateTime? desde = null, bool soloNoRevisados = true)
        {
            if (!PuedeRevisarFeedback())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            return Ok(feedback.LeerFeedback(desde ?? DateTime.Today.AddDays(-7), soloNoRevisados));
        }

        // POST api/Novedades/Comentarios/7/Revisado  (desarrollo: Dirección / Informática)
        [HttpPost]
        [Authorize]
        [Route("api/Novedades/Comentarios/{id:int}/Revisado")]
        public IHttpActionResult PostRevisado(int id)
        {
            if (!PuedeRevisarFeedback())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            return feedback.MarcarRevisado(id) ? (IHttpActionResult)StatusCode(HttpStatusCode.NoContent) : NotFound();
        }

        private bool PuedeRevisarFeedback() =>
            User != null && (User.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION) || User.IsInRoleSinDominio(GRUPO_INFORMATICA));

        #endregion
    }
}
