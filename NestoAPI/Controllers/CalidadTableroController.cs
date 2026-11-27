using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Calidad.Tablero;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class LanzamientoTableroDTO
    {
        public string JobId { get; set; }
        public bool Programado { get; set; }
        public int Semanas { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>
    /// NestoAPI#914, el tablero de KPI con nota semanal. Cada persona ve solo los suyos (Mios); Dirección e Informática, los de todos en una
    /// vista aparte. Las llamadas solo leen dbo.CalidadKpiSemanas/CalidadKpiUmbrales y el valor en curso cacheado. Sin el script, 503.
    /// Definiciones en docs/claude/calidad-kpi.md, «Tablero de KPI con nota semanal».
    /// </summary>
    [Authorize]
    public class CalidadTableroController : ApiController
    {
        public const string MENSAJE_SOLO_DIRECCION = "Solo Dirección o Informática pueden ver los indicadores de todos.";
        public const string MENSAJE_SIN_PERMISO_OTRO = "Solo puede ver sus propios indicadores. Los de otra persona o de otro almacén solo los ven Dirección e Informática.";
        public const string MENSAJE_SIN_PERMISO_UMBRALES = "Solo Dirección o Informática pueden proponer y aprobar los umbrales de las notas.";
        public const string MENSAJE_SIN_PERMISO_RECALCULAR = "Solo Dirección o Informática pueden recalcular el tablero.";
        public const string MENSAJE_NO_SE_PUEDE_LEER = "No se ha podido leer el tablero de indicadores ahora mismo. Inténtelo de nuevo en unos minutos y, si sigue, avise a Informática.";
        public const int SEMANAS_POR_DEFECTO = 12;

        private readonly Func<IRepositorioTablero> repositorio;
        private readonly Func<IFuentesKpi> fuentes;
        private readonly IProgramadorTablero programador;
        private readonly Func<DateTime> ahoraMadrid;
        private readonly Func<IPrincipal, Task<IdentidadTablero>> identidad;

        public CalidadTableroController() : this(null)
        {
        }

        internal CalidadTableroController(Func<IRepositorioTablero> repositorio, Func<IFuentesKpi> fuentes = null, IProgramadorTablero programador = null,
            Func<DateTime> ahoraMadrid = null, Func<IPrincipal, Task<IdentidadTablero>> identidad = null)
        {
            this.repositorio = repositorio ?? (() => RepositorioTableroSql.DesdeConfiguracion());
            this.fuentes = fuentes ?? (() => FuentesKpiSql.DesdeConfiguracion());
            this.programador = programador ?? new ProgramadorTableroHangfire();
            this.ahoraMadrid = ahoraMadrid ?? FuentesKpiSql.AhoraMadrid;
            this.identidad = identidad ?? ResolverIdentidad;
        }

        internal static bool PuedeVerTodo(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        private static Task<IdentidadTablero> ResolverIdentidad(IPrincipal usuario) =>
            ResolutorIdentidadTablero.Resolver(usuario,
                async u =>
                {
                    using (var db = new NVEntities())
                    {
                        return await new ServicioVendedoresVisibles(db).VendedoresDelUsuario(u, Constantes.Empresas.EMPRESA_POR_DEFECTO).ConfigureAwait(false);
                    }
                },
                nombre => ParametrosUsuarioController.LeerParametro(Constantes.Empresas.EMPRESA_POR_DEFECTO, nombre, ResolutorIdentidadTablero.CLAVE_ALMACEN_USUARIO));

        private ServicioTablero Servicio(IRepositorioTablero repo) => new ServicioTablero(repo, fuentes, ahoraMadrid);

        /// <summary>El repositorio si las tablas existen; si no, la respuesta 503 (y null).</summary>
        private IRepositorioTablero Repositorio(out IHttpActionResult error)
        {
            error = null;
            try
            {
                IRepositorioTablero repo = repositorio();
                if (!repo.TablasExisten())
                {
                    error = Content(HttpStatusCode.ServiceUnavailable, TableroKpiJobsService.MENSAJE_SIN_TABLAS);
                    return null;
                }
                return repo;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Tablero #914] {ex.Message}", ex));
                error = Content(HttpStatusCode.ServiceUnavailable, MENSAJE_NO_SE_PUEDE_LEER);
                return null;
            }
        }

        private IHttpActionResult Leer(Func<IHttpActionResult> leer)
        {
            try
            {
                return leer();
            }
            catch (ErrorTableroException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Tablero #914] {ex.Message}", ex));
                return Content(HttpStatusCode.ServiceUnavailable, MENSAJE_NO_SE_PUEDE_LEER);
            }
        }

        // GET api/Calidad/Tablero/Mios
        /// <summary>Los indicadores de quien llama (nunca los de otra persona). Sin ninguno, lista vacía.</summary>
        [HttpGet]
        [Route("api/Calidad/Tablero/Mios")]
        [ResponseType(typeof(List<KpiMioDTO>))]
        public async Task<IHttpActionResult> GetMios()
        {
            IRepositorioTablero repo = Repositorio(out IHttpActionResult error);
            if (repo == null)
            {
                return error;
            }
            IdentidadTablero quien = await identidad(User).ConfigureAwait(false);
            return Leer(() => Ok(Servicio(repo).Mios(quien)));
        }

        // GET api/Calidad/Tablero/PedidoPerfecto/Semanas?semanas=12&tipoDueno=Vendedor&dueno=JE
        /// <summary>
        /// La evolución semanal de un indicador. Sin <paramref name="dueno"/>, la del propio usuario; con un dueño que no es el suyo, solo Dirección
        /// e Informática (403).
        /// </summary>
        [HttpGet]
        [Route("api/Calidad/Tablero/{kpi}/Semanas")]
        [ResponseType(typeof(EvolucionKpiDTO))]
        public async Task<IHttpActionResult> GetSemanas(string kpi, int semanas = SEMANAS_POR_DEFECTO, string tipoDueno = null, string dueno = null)
        {
            IKpiTablero indicador = CatalogoKpiTablero.Buscar(kpi);
            if (indicador == null)
            {
                return Content(HttpStatusCode.NotFound, $"No existe el indicador «{kpi}». Son: {string.Join(", ", CatalogoKpiTablero.Todos.Select(k => k.Clave))}.");
            }
            if (semanas < 1 || semanas > ServicioTablero.SEMANAS_MAXIMAS)
            {
                return BadRequest($"«semanas» tiene que estar entre 1 y {ServicioTablero.SEMANAS_MAXIMAS}.");
            }
            TipoDuenoKpi? tipo = null;
            if (!string.IsNullOrWhiteSpace(tipoDueno))
            {
                if (!Enum.TryParse(tipoDueno.Trim(), true, out TipoDuenoKpi t) || !indicador.TiposDueno.Contains(t))
                {
                    return BadRequest($"El tipo de dueño de «{indicador.Clave}» tiene que ser {string.Join(" o ", indicador.TiposDueno)}.");
                }
                tipo = t;
            }
            IRepositorioTablero repo = Repositorio(out IHttpActionResult error);
            if (repo == null)
            {
                return error;
            }
            IdentidadTablero quien = await identidad(User).ConfigureAwait(false);
            TipoDuenoKpi usar;
            string elDueno;
            if (!string.IsNullOrWhiteSpace(dueno))
            {
                usar = tipo ?? indicador.TiposDueno[0];
                elDueno = dueno.Trim();
                if (!quien.EsSuyo(indicador, usar, elDueno) && !quien.PuedeVerTodo)
                {
                    return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO_OTRO);
                }
            }
            else
            {
                DuenoKpi propio = quien.Duenos(indicador).FirstOrDefault(d => !tipo.HasValue || d.Tipo == tipo.Value);
                usar = propio?.Tipo ?? tipo ?? indicador.TiposDueno[0];
                elDueno = propio?.Dueno;
            }
            return Leer(() => Ok(Servicio(repo).Evolucion(indicador, usar, elDueno, semanas)));
        }

        // GET api/Calidad/Tablero?semana=2026-11-16
        /// <summary>Todos los indicadores y todos los dueños de una semana, con su nota (Dirección e Informática). Sin semana, la pasada.</summary>
        [HttpGet]
        [Route("api/Calidad/Tablero")]
        [ResponseType(typeof(TableroSemanaDTO))]
        public IHttpActionResult GetTablero(DateTime? semana = null)
        {
            if (!PuedeVerTodo(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SOLO_DIRECCION);
            }
            DateTime hoy = ahoraMadrid().Date;
            DateTime lunes = SemanaKpi.LunesDe(semana ?? hoy.AddDays(-7));
            if (lunes > hoy)
            {
                return BadRequest("Esa semana todavía no ha empezado.");
            }
            IRepositorioTablero repo = Repositorio(out IHttpActionResult error);
            if (repo == null)
            {
                return error;
            }
            return Leer(() => Ok(Servicio(repo).Tablero(lunes)));
        }

        // GET api/Calidad/Tablero/Umbrales/Propuesta?kpi=PedidoPerfecto[&tipoDueno=Vendedor]
        /// <summary>Con las semanas guardadas, los tres cortes propuestos (percentiles 25, 50 y 75 del sentido bueno) y cuántas caerían en cada nota.</summary>
        [HttpGet]
        [Route("api/Calidad/Tablero/Umbrales/Propuesta")]
        [ResponseType(typeof(PropuestaUmbralesDTO))]
        public IHttpActionResult GetPropuesta(string kpi, string tipoDueno = null)
        {
            if (!PuedeVerTodo(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO_UMBRALES);
            }
            IKpiTablero indicador = CatalogoKpiTablero.Buscar(kpi);
            if (indicador == null)
            {
                return Content(HttpStatusCode.NotFound, $"No existe el indicador «{kpi}». Son: {string.Join(", ", CatalogoKpiTablero.Todos.Select(k => k.Clave))}.");
            }
            TipoDuenoKpi? tipo = null;
            if (!string.IsNullOrWhiteSpace(tipoDueno))
            {
                if (!Enum.TryParse(tipoDueno.Trim(), true, out TipoDuenoKpi t) || !indicador.TiposDueno.Contains(t))
                {
                    return BadRequest($"El tipo de dueño de «{indicador.Clave}» tiene que ser {string.Join(" o ", indicador.TiposDueno)}.");
                }
                tipo = t;
            }
            IRepositorioTablero repo = Repositorio(out IHttpActionResult error);
            if (repo == null)
            {
                return error;
            }
            return Leer(() => Ok(Servicio(repo).Propuesta(indicador, tipo)));
        }

        // PUT api/Calidad/Tablero/Umbrales {Kpi, TipoDueno, CortePoorFair, CorteFairGood, CorteGoodFantastic, VigenteDesde?}
        /// <summary>Aprueba los umbrales de un indicador (rastro en Modificaciones). Un cambio vale desde esta semana: no cambia las notas pasadas.</summary>
        [HttpPut]
        [Route("api/Calidad/Tablero/Umbrales")]
        [ResponseType(typeof(UmbralesDTO))]
        public IHttpActionResult PutUmbrales([FromBody] AprobarUmbralesDTO peticion)
        {
            if (!PuedeVerTodo(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO_UMBRALES);
            }
            IRepositorioTablero repo = Repositorio(out IHttpActionResult error);
            if (repo == null)
            {
                return error;
            }
            string usuario = UsuarioAuditoriaHelper.ParaAuditoria(UsuarioAuditoriaHelper.Resolver(User, null));
            return Leer(() => Ok(UmbralesDTO.De(Servicio(repo).Aprobar(peticion, usuario))));
        }

        // POST api/Calidad/Tablero/Recalcular?semanas=12&ahora=false
        /// <summary>
        /// Recalcula todas las semanas cerradas de las últimas <paramref name="semanas"/> (1-104) para el relleno inicial (202). Se ENCOLA: de lunes a
        /// viernes de 8 a 20 (Madrid) se programa para las 21:00, salvo <paramref name="ahora"/> = true.
        /// </summary>
        [HttpPost]
        [Route("api/Calidad/Tablero/Recalcular")]
        [ResponseType(typeof(LanzamientoTableroDTO))]
        public IHttpActionResult PostRecalcular(int semanas = SEMANAS_POR_DEFECTO, bool ahora = false)
        {
            if (!PuedeVerTodo(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO_RECALCULAR);
            }
            if (semanas < 1 || semanas > ServicioTablero.SEMANAS_MAXIMAS)
            {
                return BadRequest($"«semanas» tiene que estar entre 1 y {ServicioTablero.SEMANAS_MAXIMAS}.");
            }
            IRepositorioTablero repo = Repositorio(out IHttpActionResult error);
            if (repo == null)
            {
                return error;
            }
            string usuario = User?.Identity?.Name;
            DateTime madrid = ahoraMadrid();
            if (!ahora && HorarioHistoricoCorreos.EnHorario(madrid))
            {
                DateTime cuando = HorarioHistoricoCorreos.CuandoEmpezar(madrid);
                return Content(HttpStatusCode.Accepted, new LanzamientoTableroDTO
                {
                    JobId = programador.Programar(cuando, semanas, usuario),
                    Programado = true,
                    Semanas = semanas,
                    Mensaje = $"Es horario de trabajo: el tablero se recalculará a las {cuando:HH:mm} (hora de Madrid)."
                });
            }
            return Content(HttpStatusCode.Accepted, new LanzamientoTableroDTO
            {
                JobId = programador.Encolar(semanas, usuario),
                Programado = false,
                Semanas = semanas,
                Mensaje = "Recálculo del tablero encolado: en unos minutos estará en GET api/Calidad/Tablero."
            });
        }
    }
}
