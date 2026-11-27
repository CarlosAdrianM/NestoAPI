using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Calidad.Encuestas;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#914 (encuestas a clientes, corte E1 EN SOMBRA): SOLO LECTURA, Dirección e Informática (los permisos de api/Calidad/PedidoPerfecto).
    /// No envía nada a ningún cliente. Definiciones en docs/claude/calidad-kpi.md.
    /// </summary>
    [Authorize]
    public class CalidadEncuestasController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO = "Solo Dirección o Informática pueden ver las encuestas a clientes.";
        public const string MENSAJE_NO_SE_PUEDE_LEER = "No se han podido calcular las encuestas ahora mismo. Inténtelo de nuevo en unos minutos y, si sigue, avise a Informática.";
        public const int DIAS_POR_DEFECTO = 90;
        /// <summary>Tope del rango de la simulación.</summary>
        public const int DIAS_MAXIMOS = 180;
        public const int DIAS_SOMBRA_POR_DEFECTO = 30;
        public const int DIAS_SOMBRA_MAXIMOS = 365;
        public const int INTERVALOS_MAXIMOS_COMPARAR = 5;

        private static int avisadoSinTablas;

        private readonly Func<IServicioEncuestas> servicio;
        private readonly Func<DateTime> hoyMadrid;

        public CalidadEncuestasController() : this(null)
        {
        }

        internal CalidadEncuestasController(Func<IServicioEncuestas> servicio, Func<DateTime> hoyMadrid = null)
        {
            this.servicio = servicio ?? (() => new ServicioEncuestas(ahora: () => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Startup.ZonaHorariaPeninsular())));
            this.hoyMadrid = hoyMadrid ?? (() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Startup.ZonaHorariaPeninsular()).Date);
        }

        // GET api/Calidad/Encuestas/Simulacion?desde=2026-08-29&hasta=2026-11-27&diasEntre=60&comparar=30,45,60
        [HttpGet]
        [Route("api/Calidad/Encuestas/Simulacion")]
        [ResponseType(typeof(SimulacionEncuestasDTO))]
        public IHttpActionResult GetSimulacion(DateTime? desde = null, DateTime? hasta = null, int? diasEntre = null, string comparar = null,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (!CalidadPedidoPerfectoController.PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            List<int> intervalos = null;
            string error = Periodo(desde, hasta, hoyMadrid(), out DateTime inicio, out DateTime fin)
                ?? ValidarDiasEntre(diasEntre)
                ?? Intervalos(comparar, out intervalos);
            if (error != null)
            {
                return BadRequest(error);
            }
            string emp = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            return Calcular(() => intervalos == null
                ? (IHttpActionResult)Ok(servicio().Simular(emp, inicio, fin, diasEntre))
                : Ok(servicio().Comparar(emp, inicio, fin, intervalos)));
        }

        // GET api/Calidad/Encuestas/Sombra?dias=30
        [HttpGet]
        [Route("api/Calidad/Encuestas/Sombra")]
        [ResponseType(typeof(SimulacionEncuestasDTO))]
        public IHttpActionResult GetSombra(int dias = DIAS_SOMBRA_POR_DEFECTO, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (!CalidadPedidoPerfectoController.PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (dias < 1 || dias > DIAS_SOMBRA_MAXIMOS)
            {
                return BadRequest($"Los días tienen que estar entre 1 y {DIAS_SOMBRA_MAXIMOS}.");
            }
            string emp = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            try
            {
                return Ok(servicio().Sombra(emp, dias));
            }
            catch (EncuestasSinTablasException ex)
            {
                if (Interlocked.Exchange(ref avisadoSinTablas, 1) == 0)
                {
                    ElmahHelper.Log(new Exception("[Encuestas #914] " + ex.Message + " Este aviso sale una sola vez.", ex));
                }
                return Content(HttpStatusCode.ServiceUnavailable, ServicioEncuestas.MENSAJE_SIN_TABLAS);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Encuestas #914] " + ex.Message, ex));
                return Content(HttpStatusCode.ServiceUnavailable, MENSAJE_NO_SE_PUEDE_LEER);
            }
        }

        /// <summary>Días de candidatura [desde, hasta): por defecto los últimos 90 hasta hoy; como mucho 180; hasta ≤ hoy. Null si vale.</summary>
        internal static string Periodo(DateTime? desde, DateTime? hasta, DateTime hoy, out DateTime inicio, out DateTime fin)
        {
            fin = (hasta ?? hoy).Date;
            inicio = (desde ?? fin.AddDays(-DIAS_POR_DEFECTO)).Date;
            if (inicio >= fin)
            {
                return "La fecha «desde» tiene que ser anterior a «hasta».";
            }
            if ((fin - inicio).TotalDays > DIAS_MAXIMOS)
            {
                return $"El periodo no puede pasar de {DIAS_MAXIMOS} días: acórtelo o pídalo por partes.";
            }
            if (fin > hoy.Date)
            {
                return "La fecha «hasta» no puede ser posterior a hoy.";
            }
            return null;
        }

        internal static string ValidarDiasEntre(int? diasEntre) =>
            diasEntre.HasValue && (diasEntre.Value < ParametrosEncuestas.DIAS_ENTRE_MINIMO || diasEntre.Value > ParametrosEncuestas.DIAS_ENTRE_MAXIMO)
                ? $"Los días entre encuestas tienen que estar entre {ParametrosEncuestas.DIAS_ENTRE_MINIMO} y {ParametrosEncuestas.DIAS_ENTRE_MAXIMO}."
                : null;

        /// <summary>«30,45,60» → [30, 45, 60]. Vacío: null (sin comparación). Cada uno entre 30 y 180; como mucho 5. Null si vale.</summary>
        internal static string Intervalos(string comparar, out List<int> intervalos)
        {
            intervalos = null;
            if (string.IsNullOrWhiteSpace(comparar))
            {
                return null;
            }
            var lista = new List<int>();
            foreach (string trozo in comparar.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(trozo.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int dias))
                {
                    return $"«{trozo.Trim()}» no es un número de días. Escriba los intervalos separados por comas, por ejemplo 30,45,60.";
                }
                string error = ValidarDiasEntre(dias);
                if (error != null)
                {
                    return error;
                }
                if (!lista.Contains(dias))
                {
                    lista.Add(dias);
                }
            }
            if (lista.Count > INTERVALOS_MAXIMOS_COMPARAR)
            {
                return $"Como mucho se pueden comparar {INTERVALOS_MAXIMOS_COMPARAR} intervalos a la vez.";
            }
            intervalos = lista.OrderBy(i => i).ToList();
            return null;
        }

        private IHttpActionResult Calcular(Func<IHttpActionResult> calculo)
        {
            try
            {
                return calculo();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Encuestas #914] " + ex.Message, ex));
                return Content(HttpStatusCode.ServiceUnavailable, MENSAJE_NO_SE_PUEDE_LEER);
            }
        }
    }
}
