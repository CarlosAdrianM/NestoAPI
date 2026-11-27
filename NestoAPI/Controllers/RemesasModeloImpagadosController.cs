using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using NestoAPI.Infraestructure.Remesas;
using NestoAPI.Infraestructure.Remesas.ModeloImpagados;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.Remesas;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#896: el modelo de probabilidad de devolución de los recibos de una remesa, EN SOMBRA. Aquí solo se mira cómo va
    /// (Dirección e Informática) y se lanza el entrenamiento a mano; nada de esto se enseña todavía en Nesto ni en NestoApp.
    /// </summary>
    public class RemesasModeloImpagadosController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO = "Solo Dirección o Informática pueden ver el modelo de impagados de las remesas.";
        public const string MENSAJE_SIN_PERMISO_ENTRENAR = "Solo Dirección o Informática pueden entrenar el modelo de impagados de las remesas.";

        private readonly Func<IRepositorioModeloImpagos> repositorio;
        private readonly IProgramadorModeloImpagos programador;
        private readonly Func<DateTime> ahoraMadrid;
        private readonly Func<AlmacenModeloImpagos> almacen;
        private readonly Func<bool> ensenar;
        private readonly Func<IRemesasService> remesas;
        private readonly Func<AlmacenModeloImpagos, IPredictorImpagos> predictor;

        public RemesasModeloImpagadosController() : this(null)
        {
        }

        internal RemesasModeloImpagadosController(Func<IRepositorioModeloImpagos> repositorio, IProgramadorModeloImpagos programador = null,
            Func<DateTime> ahoraMadrid = null, Func<AlmacenModeloImpagos> almacen = null, Func<bool> ensenar = null,
            Func<IRemesasService> remesas = null, Func<AlmacenModeloImpagos, IPredictorImpagos> predictor = null)
        {
            this.repositorio = repositorio ?? (() => RepositorioModeloImpagos.DesdeConfiguracion());
            this.programador = programador ?? new ProgramadorModeloImpagosHangfire();
            this.ahoraMadrid = ahoraMadrid ?? (() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Startup.ZonaHorariaPeninsular()));
            this.almacen = almacen ?? (() => AlmacenModeloImpagos.Produccion());
            this.ensenar = ensenar ?? (() => InterruptorEnsenarImpagos.Encendido);
            this.remesas = remesas ?? (() => new RemesasService());
            this.predictor = predictor ?? (a => new PredictorImpagos(a));
        }

        internal static bool PuedeVer(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        // GET api/Remesas/ModeloImpagados/Comparacion?semanas=12
        /// <summary>
        /// El último entrenamiento (métricas de validación del modelo y de LA REGLA simple), lo REAL de la sombra en las últimas
        /// <paramref name="semanas"/> (1-104; previsto frente a real por tramos), y si el modelo cumple «fiable» (AUC ≥ 0,80 y calibración
        /// dentro de ±25 % en los tramos altos durante 4 semanas seguidas) y desde cuándo. 503 sin el script.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Remesas/ModeloImpagados/Comparacion")]
        [ResponseType(typeof(ComparacionModeloImpagosDTO))]
        public IHttpActionResult GetComparacion(int semanas = ComparacionModeloImpagos.SEMANAS_POR_DEFECTO)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            IRepositorioModeloImpagos repo = repositorio();
            if (!repo.TablaEntrenamientosExiste() || !repo.TablaPrediccionesExiste())
            {
                return Content(HttpStatusCode.ServiceUnavailable, ModeloImpagosRemesasJobsService.MENSAJE_SIN_TABLA);
            }
            AlmacenModeloImpagos guardado = almacen();
            return Ok(ComparacionModeloImpagos.Construir(repo, ahoraMadrid().Date, semanas, guardado.HayModelo, guardado.HayRegla, ensenar()));
        }

        // GET api/Remesas/ModeloImpagados/Candidatos?empresa=1&hasta=2026-11-30
        /// <summary>
        /// NestoAPI#896 (27/11/26): el riesgo de devolución de los efectos candidatos a remesa ANTES de girar (Dirección e Informática).
        /// Mismos parámetros y candidatos que GET api/Remesas/EfectosCandidatos; para cada uno, lo mismo que apuntaría la sombra si la
        /// remesa se creara hoy. No depende del interruptor de enseñar y no escribe nada. 503 sin el script.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Remesas/ModeloImpagados/Candidatos")]
        [ResponseType(typeof(CandidatosRiesgoImpagosDTO))]
        public async Task<IHttpActionResult> GetCandidatos(string empresa, DateTime? hasta = null)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            IRepositorioModeloImpagos repo = repositorio();
            if (!repo.TablaEntrenamientosExiste() || !repo.TablaPrediccionesExiste())
            {
                return Content(HttpStatusCode.ServiceUnavailable, ModeloImpagosRemesasJobsService.MENSAJE_SIN_TABLA);
            }
            List<EfectoCandidatoDTO> candidatos = await remesas().LeerEfectosCandidatosSepaAsync(empresa, hasta).ConfigureAwait(false);
            return Ok(CandidatosRiesgoImpagos.Construir(repo, predictor(almacen()), empresa, candidatos, ahoraMadrid()));
        }

        // POST api/Remesas/ModeloImpagados/Entrenar?ahora=false
        /// <summary>
        /// Entrena el modelo de impagados (202). De lunes a viernes de 8 a 20 (Madrid) se programa para las 21:00, salvo
        /// <paramref name="ahora"/> = true. 503 sin el script.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Remesas/ModeloImpagados/Entrenar")]
        [ResponseType(typeof(LanzamientoModeloImpagosDTO))]
        public IHttpActionResult PostEntrenar(bool ahora = false)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO_ENTRENAR);
            }
            if (!repositorio().TablaEntrenamientosExiste())
            {
                return Content(HttpStatusCode.ServiceUnavailable, ModeloImpagosRemesasJobsService.MENSAJE_SIN_TABLA);
            }
            string usuario = User?.Identity?.Name;
            DateTime madrid = ahoraMadrid();
            if (!ahora && HorarioHistoricoCorreos.EnHorario(madrid))
            {
                DateTime cuando = HorarioHistoricoCorreos.CuandoEmpezar(madrid);
                return Content(HttpStatusCode.Accepted, new LanzamientoModeloImpagosDTO
                {
                    JobId = programador.Programar(cuando, usuario),
                    Programado = true,
                    Mensaje = $"Es horario de trabajo: el modelo de impagados se entrenará a las {cuando:HH:mm} (hora de Madrid). El resultado, en GET api/Remesas/ModeloImpagados/Comparacion."
                });
            }
            return Content(HttpStatusCode.Accepted, new LanzamientoModeloImpagosDTO
            {
                JobId = programador.Encolar(usuario),
                Programado = false,
                Mensaje = "Entrenamiento del modelo de impagados encolado: en unos minutos, el resultado en GET api/Remesas/ModeloImpagados/Comparacion."
            });
        }
    }
}
