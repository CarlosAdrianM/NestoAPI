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

        public NovedadesController() : this(new ServicioNovedades(), new ServicioFeedbackNovedades()) { }

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

            // APAÑO TEMPORAL (23/09/26, NestoApp#186): la NestoApp publicada (2.20.5) pide sin ámbito y
            // filtra en cliente, así que desde f167cd3e (sin ámbito = escritorio) no le llega ninguna
            // novedad suya. Nesto llama con HttpClient de .NET (sin User-Agent de navegador); la app
            // llama desde el WebView del móvil (User-Agent «Mozilla/...»). Se retira cuando todas las
            // NestoApp en uso manden ?ambito=NestoApp.
            if (string.IsNullOrWhiteSpace(ambito) && EsLlamadaDesdeNavegador(Request))
            {
                ambito = AMBITO_NESTOAPP;
            }

            // NestoAPI#489: el ámbito se filtra ANTES que la versión. Nesto (1.10.x) y NestoApp (2.x)
            // tienen espacios de versiones distintos: comparar desdeVersion sin acotar el producto
            // descartaría o colaría entradas del otro.
            if (!string.IsNullOrWhiteSpace(ambito))
            {
                string ambitoBuscado = ambito.Trim();
                novedades = novedades
                    .Where(n => string.Equals(n.Ambito?.Trim(), ambitoBuscado, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                // Sin ámbito (Nesto de escritorio, que solo manda desdeVersion=1.10.x): NUNCA las de la app.
                // El 17/09/26 se colaron las 2.20.x de NestoApp en el popup de Nesto porque 2.20 > 1.10.
                novedades = novedades
                    .Where(n => !string.Equals(n.Ambito?.Trim(), AMBITO_NESTOAPP, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

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

            return Ok(ConFeedback(ordenadas));
        }

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
                return novedades;
            }
            Dictionary<int, ResumenFeedbackNovedad> porNovedad = resumen.GroupBy(r => r.NovedadId).ToDictionary(g => g.Key, g => g.First());
            return novedades.Select(n =>
            {
                NovedadConFeedbackDTO conFeedback = NovedadConFeedbackDTO.Desde(n);
                porNovedad.TryGetValue(n.Id, out ResumenFeedbackNovedad r);
                conFeedback.VotosPositivos = r?.Positivos ?? 0;
                conFeedback.VotosNegativos = r?.Negativos ?? 0;
                conFeedback.MiVoto = r?.MiVoto;
                conFeedback.NumeroComentarios = r?.Comentarios ?? 0;
                return (NovedadDTO)conFeedback;
            }).ToList();
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
        public IHttpActionResult PostComentario(int id, [FromBody] NuevoComentarioNovedadDTO comentario)
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
