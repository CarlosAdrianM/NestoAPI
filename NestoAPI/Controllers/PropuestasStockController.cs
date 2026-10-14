using Hangfire;
using NestoAPI.Infrastructure;
using NestoAPI.Infraestructure.Stocks.Tendencias;
using System;
using System.Collections.Generic;
using System.Net;
using System.Web.Http;
using static NestoAPI.Models.Constantes;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#630 (stock obsoleto, corte 2): propuestas mensuales de máximo y mínimo por producto. Solo Dirección.
    /// </summary>
    [RoutePrefix("api/PropuestasStock")]
    public class PropuestasStockController : ApiController
    {
        /// <summary>Antes de esta hora o desde <see cref="HORA_FIN_HORARIO"/>, «Generar» se encola ya; entre medias, se programa para la madrugada.</summary>
        internal const int HORA_INICIO_HORARIO = 6;
        internal const int HORA_FIN_HORARIO = 22;
        internal const int HORA_PROGRAMADA = 4;
        internal const int MINUTO_PROGRAMADO = 30;
        internal const int TOP_MAXIMO = 50000;

        private readonly Func<string, string, string> encolar;
        private readonly Func<string, string, DateTimeOffset, string> programar;
        private readonly Func<IRepositorioTendencias> crearRepositorio;
        private readonly Func<DateTime> ahora;

        public PropuestasStockController() : this(null)
        {
        }

        /// <param name="encolar">(mes, usuario) → id del job de Hangfire.</param>
        /// <param name="programar">(mes, usuario, cuándo) → id del job programado.</param>
        internal PropuestasStockController(Func<string, string, string> encolar, Func<string, string, DateTimeOffset, string> programar = null,
            Func<IRepositorioTendencias> crearRepositorio = null, Func<DateTime> ahora = null)
        {
            this.encolar = encolar ?? ((mes, usuario) => BackgroundJob.Enqueue(() => PropuestasStockJobsService.GenerarManual(mes, usuario)));
            this.programar = programar ?? ((mes, usuario, cuando) => BackgroundJob.Schedule(() => PropuestasStockJobsService.GenerarManual(mes, usuario), cuando));
            this.crearRepositorio = crearRepositorio ?? (() => new RepositorioTendenciasSql());
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        private bool EsDireccion() => User != null && User.IsInRoleSinDominio(GruposSeguridad.DIRECCION);

        /// <summary>
        /// Lanza a mano el job de propuestas (el mismo que corre el día 1 a las 04:30) para el mes AAAA-MM (por defecto, el último
        /// cerrado). No es síncrono: lo encola en Hangfire (cola «entrenamiento») y el Excel llega por correo. Como lee la venta y el
        /// extracto de 5 años de todos los productos, en horario de trabajo (de 6 a 22) se programa para las 04:30 siguientes,
        /// salvo <paramref name="ahora"/> = true.
        /// POST api/PropuestasStock/Generar?mes=2026-10
        /// </summary>
        [HttpPost]
        [Route("Generar")]
        [Authorize]
        public IHttpActionResult Generar(string mes = null, bool ahora = false)
        {
            if (!EsDireccion())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            DateTime momento = this.ahora();
            DateTime? referencia = string.IsNullOrWhiteSpace(mes) ? PropuestasStockJobsService.MesCerrado(momento) : PropuestasStockJobsService.LeerMes(mes);
            if (!referencia.HasValue)
            {
                return BadRequest("El mes tiene que ir como AAAA-MM (por ejemplo, 2026-10).");
            }
            if (referencia.Value > new DateTime(momento.Year, momento.Month, 1))
            {
                return BadRequest("No se pueden calcular propuestas de un mes que aún no ha empezado.");
            }
            string textoMes = referencia.Value.ToString("yyyy-MM");
            string usuario = User.Identity?.Name;
            try
            {
                bool enHorario = momento.Hour >= HORA_INICIO_HORARIO && momento.Hour < HORA_FIN_HORARIO;
                if (ahora || !enHorario)
                {
                    return Content(HttpStatusCode.Accepted, new
                    {
                        JobId = encolar(textoMes, usuario),
                        Mes = textoMes,
                        Programado = (DateTime?)null,
                        Mensaje = $"Propuestas de {textoMes} encoladas. El Excel llegará por correo."
                    });
                }
                DateTime cuando = momento.Date.AddDays(1).AddHours(HORA_PROGRAMADA).AddMinutes(MINUTO_PROGRAMADO);
                return Content(HttpStatusCode.Accepted, new
                {
                    JobId = programar(textoMes, usuario, new DateTimeOffset(cuando)),
                    Mes = textoMes,
                    Programado = (DateTime?)cuando,
                    Mensaje = $"Propuestas de {textoMes} programadas para el {cuando:dd/MM/yyyy} a las {cuando:HH:mm} (en horario no se lanza: carga la BD). Con ahora=true se lanzan ya."
                });
            }
            catch (InvalidOperationException ex)
            {
                // Sin Hangfire (en DEBUG no se arranca) no hay dónde encolarlo.
                return Content(HttpStatusCode.ServiceUnavailable, new { Message = "No se pueden encolar las propuestas de stock: " + ex.Message });
            }
        }

        /// <summary>
        /// Las propuestas guardadas de un mes (AAAA-MM; por defecto el último guardado), de más a menos exceso valorado.
        /// GET api/PropuestasStock?mes=2026-10&amp;top=50
        /// </summary>
        [HttpGet]
        [Route("")]
        [Authorize]
        public IHttpActionResult Get(string mes = null, int top = 50)
        {
            if (!EsDireccion())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            DateTime? referencia = null;
            if (!string.IsNullOrWhiteSpace(mes))
            {
                referencia = PropuestasStockJobsService.LeerMes(mes);
                if (!referencia.HasValue)
                {
                    return BadRequest("El mes tiene que ir como AAAA-MM (por ejemplo, 2026-10).");
                }
            }
            if (top <= 0)
            {
                return BadRequest("top tiene que ser mayor que 0.");
            }
            IRepositorioTendencias repositorio = crearRepositorio();
            if (!repositorio.ExisteTabla())
            {
                return Content(HttpStatusCode.NotFound, new { Message = "La tabla PropuestasStock no existe todavía (Scripts/Issue630_PropuestasStock.sql)." });
            }
            List<PropuestaStockGuardada> filas = repositorio.LeerPropuestas(referencia, Math.Min(top, TOP_MAXIMO));
            return Ok(filas);
        }
    }
}
