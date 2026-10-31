using NestoAPI.Infraestructure.Globos;
using NestoAPI.Models.Globos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#774 (Carlos 31/10/26): globos («consejos», coach marks) para enseñar lo nuevo dentro de cada app. El contenido se
    /// define UNA vez aquí (tabla Globos, Scripts/Globos.sql) y Nesto, Ariadna (y más adelante TNV y NestoApp) solo lo pintan.
    /// «Visto» es por USUARIO (del token), no por aparato: con el cambio rápido de usuario de Ariadna cada mozo ve los suyos.
    /// Gemelas: Nesto#592, Ariadna#75.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Globos")]
    public class GlobosController : ApiController
    {
        private readonly IRepositorioGlobos repositorio;

        public GlobosController(IRepositorioGlobos repositorio)
        {
            this.repositorio = repositorio;
        }

        /// <summary>Para los tests: el día de hoy.</summary>
        internal Func<DateTime> Hoy { get; set; } = () => DateTime.Today;

        // GET api/Globos?aplicacion=Ariadna&pantalla=Reparto&version=2.27
        /// <summary>
        /// Los pendientes del usuario en esa pantalla (sin pantalla: todos los de la aplicación), por Orden. Sin el script, lista
        /// vacía (las apps siguen como siempre).
        /// </summary>
        [HttpGet]
        [Route("")]
        [ResponseType(typeof(List<GloboDTO>))]
        public IHttpActionResult GetGlobos(string aplicacion = null, string pantalla = null, string version = null)
        {
            string usuario = ReglasGlobos.UsuarioVisto(User);
            if (usuario == null)
            {
                return Unauthorized();
            }
            string app = ReglasGlobos.Aplicacion(aplicacion);
            if (app == null)
            {
                return BadRequest($"Falta la aplicación o no es válida: {string.Join(", ", ReglasGlobos.APLICACIONES)}.");
            }
            List<GloboFila> activos = repositorio.LeerActivos(app, pantalla) ?? new List<GloboFila>();
            if (activos.Count == 0)
            {
                return Ok(new List<GloboDTO>());
            }
            HashSet<int> vistos = repositorio.LeerVistos(usuario, activos.Select(g => g.Id).ToList());
            return Ok(ReglasGlobos.Pendientes(activos, app, pantalla, version, User, vistos, Hoy()));
        }

        // POST api/Globos/5/Visto
        /// <summary>Marca el globo como visto por el usuario del token. Idempotente (la segunda vez, YaEstaba = true).</summary>
        [HttpPost]
        [Route("{id:int}/Visto")]
        [ResponseType(typeof(GloboVistoDTO))]
        public IHttpActionResult PostVisto(int id)
        {
            string usuario = ReglasGlobos.UsuarioVisto(User);
            if (usuario == null)
            {
                return Unauthorized();
            }
            switch (repositorio.MarcarVisto(usuario, id))
            {
                case ResultadoVistoGlobo.Marcado:
                    return Ok(new GloboVistoDTO { Id = id, YaEstaba = false });
                case ResultadoVistoGlobo.YaEstaba:
                    return Ok(new GloboVistoDTO { Id = id, YaEstaba = true });
                case ResultadoVistoGlobo.NoExiste:
                    return Content(HttpStatusCode.NotFound, new HttpError($"No existe el globo {id}."));
                default:
                    return BadRequest("Falta el script de los globos (Scripts/Globos.sql).");
            }
        }
    }
}
