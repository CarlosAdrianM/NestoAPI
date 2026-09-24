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
        }

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
        /// desdeVersion=1.10.x): todo MENOS las de la app. El 17/09/26 se colaron las 2.20.x de
        /// NestoApp en el popup de Nesto porque 2.20 > 1.10.
        /// </summary>
        internal static bool EsDelAmbito(string ambitoNovedad, string ambito)
        {
            return !string.IsNullOrWhiteSpace(ambito)
                ? string.Equals(ambitoNovedad?.Trim(), ambito.Trim(), StringComparison.OrdinalIgnoreCase)
                : !string.Equals(ambitoNovedad?.Trim(), AMBITO_NESTOAPP, StringComparison.OrdinalIgnoreCase);
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
            List<SugerenciaNovedadDTO> sugerencias = servicio.LeerSugerencias(incluirCerradas)
                .Where(s => EsDelAmbito(s.Ambito, ambitoEfectivo))
                .Select(s => s.ADto())
                .ToList();
            _ = RellenarFeedback(sugerencias);
            return Ok(sugerencias
                .OrderByDescending(s => (s.VotosPositivos ?? 0) - (s.VotosNegativos ?? 0))
                .ThenByDescending(s => s.SugeridaFecha)
                .ToList());
        }

        // POST api/Novedades/Sugerencias  { "Texto": "Aquí iría bien un botón...", "ImagenBase64": "...", "VersionCliente": "1.10.31.0" }
        [HttpPost]
        [Authorize]
        [Route("api/Novedades/Sugerencias")]
        [ResponseType(typeof(SugerenciaNovedadDTO))]
        public async System.Threading.Tasks.Task<IHttpActionResult> PostSugerencia([FromBody] NuevoComentarioNovedadDTO sugerencia)
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
                Ambito = cliente == ReglasFeedbackNovedades.CLIENTE_NESTOAPP ? AMBITO_NESTOAPP : "Nesto",
                Titulo = ReglasSugerenciasNovedades.TituloDesde(sugerencia.Texto),
                TextoOriginal = sugerencia.Texto.Trim(),
                Imagen = imagen,
                ImagenTipo = tipo,
                SugeridaPor = usuario,
                SugeridaNombre = ReglasFeedbackNovedades.NombreVisible(User)
            };
            int id = servicio.CrearSugerencia(aGrabar);
            // NestoAPI#537: también se puede mencionar a alguien al sugerir (el aviso lleva a la sugerencia)
            await AvisarMencionesEnSugerencia(id, aGrabar.TextoOriginal, aGrabar.SugeridaNombre, User?.Identity?.Name).ConfigureAwait(false);
            return Ok(new SugerenciaNovedadDTO
            {
                Id = id,
                Fecha = DateTime.Today,
                Categoria = ReglasSugerenciasNovedades.CATEGORIA_SUGERENCIA,
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
        public IHttpActionResult PutVoto(int id, [FromBody] VotoNovedadDTO voto)
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
