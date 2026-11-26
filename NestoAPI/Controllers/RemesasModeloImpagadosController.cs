using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using NestoAPI.Infraestructure.Remesas.ModeloImpagados;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Net;
using System.Security.Principal;
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

        public RemesasModeloImpagadosController() : this(null)
        {
        }

        internal RemesasModeloImpagadosController(Func<IRepositorioModeloImpagos> repositorio, IProgramadorModeloImpagos programador = null,
            Func<DateTime> ahoraMadrid = null, Func<AlmacenModeloImpagos> almacen = null, Func<bool> ensenar = null)
        {
            this.repositorio = repositorio ?? (() => RepositorioModeloImpagos.DesdeConfiguracion());
            this.programador = programador ?? new ProgramadorModeloImpagosHangfire();
            this.ahoraMadrid = ahoraMadrid ?? (() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Startup.ZonaHorariaPeninsular()));
            this.almacen = almacen ?? (() => AlmacenModeloImpagos.Produccion());
            this.ensenar = ensenar ?? (() => InterruptorEnsenarImpagos.Encendido);
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
