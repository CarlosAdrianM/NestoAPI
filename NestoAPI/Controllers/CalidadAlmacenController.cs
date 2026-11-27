using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Calidad.Almacen;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Linq;
using System.Net;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#914 (corte 2): indicadores del puesto de preparación (Almacén) por preparador, SOLO LECTURA y en sombra (Dirección e
    /// Informática, como el pedido perfecto). El preparador es el que registra Ariadna. Definiciones en docs/claude/calidad-kpi.md.
    /// </summary>
    [Authorize]
    public class CalidadAlmacenController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO = "Solo Dirección o Informática pueden ver los indicadores de Almacén.";
        public const string MENSAJE_NO_SE_PUEDE_LEER = "No se han podido calcular los indicadores de Almacén ahora mismo. Inténtelo de nuevo en unos minutos y, si sigue, avise a Informática.";
        /// <summary>Por defecto, los últimos 3 meses hasta ayer (hasta = hoy, que no entra). Ariadna no tiene rastro de antes del 30/09/26.</summary>
        public const int MESES_DESDE_POR_DEFECTO = 3;
        public const int MESES_MAXIMOS = 13;
        public const int TAMANO_POR_DEFECTO = 100;
        public const int TAMANO_MAXIMO = 1000;

        private readonly Func<IServicioCalidadAlmacen> servicio;
        private readonly Func<DateTime> hoyMadrid;

        public CalidadAlmacenController() : this(null)
        {
        }

        internal CalidadAlmacenController(Func<IServicioCalidadAlmacen> servicio, Func<DateTime> hoyMadrid = null)
        {
            this.servicio = servicio ?? (() => new ServicioCalidadAlmacen(ahora: () => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Startup.ZonaHorariaPeninsular())));
            this.hoyMadrid = hoyMadrid ?? (() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Startup.ZonaHorariaPeninsular()).Date);
        }

        // GET api/Calidad/Almacen/Resumen?empresa=1&desde=2026-09-30&hasta=2026-11-27
        [HttpGet]
        [Route("api/Calidad/Almacen/Resumen")]
        [ResponseType(typeof(ResumenCalidadAlmacenDTO))]
        public IHttpActionResult GetResumen(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, DateTime? desde = null, DateTime? hasta = null)
        {
            if (!CalidadPedidoPerfectoController.PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            string error = Periodo(desde, hasta, hoyMadrid(), out DateTime inicio, out DateTime fin);
            if (error != null)
            {
                return BadRequest(error);
            }
            return Calcular(() => Ok(servicio().Resumen(Empresa(empresa), inicio, fin)));
        }

        // GET api/Calidad/Almacen/Detalle?empresa=1&preparador=Andrey&desde=&hasta=&indicador=FaltaEnPacking&pagina=1&tamano=100
        [HttpGet]
        [Route("api/Calidad/Almacen/Detalle")]
        [ResponseType(typeof(DetalleCalidadAlmacenDTO))]
        public IHttpActionResult GetDetalle(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, string preparador = null, DateTime? desde = null,
            DateTime? hasta = null, string indicador = null, int pagina = 1, int tamano = TAMANO_POR_DEFECTO)
        {
            if (!CalidadPedidoPerfectoController.PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            string error = Periodo(desde, hasta, hoyMadrid(), out DateTime inicio, out DateTime fin);
            if (error != null)
            {
                return BadRequest(error);
            }
            IndicadorAlmacen? filtro = null;
            if (!string.IsNullOrWhiteSpace(indicador))
            {
                if (!Enum.TryParse(indicador.Trim(), true, out IndicadorAlmacen leido) || !Enum.IsDefined(typeof(IndicadorAlmacen), leido)
                    || int.TryParse(indicador.Trim(), out _))
                {
                    return BadRequest($"No existe el indicador «{indicador.Trim()}». Los indicadores son: " +
                        string.Join(", ", InfoIndicadoresAlmacen.Todos.Select(i => i.ToString())) + ".");
                }
                filtro = leido;
            }
            if (pagina < 1)
            {
                return BadRequest("La página tiene que ser 1 o más.");
            }
            if (tamano < 1 || tamano > TAMANO_MAXIMO)
            {
                return BadRequest($"El tamaño de la página tiene que estar entre 1 y {TAMANO_MAXIMO}.");
            }
            return Calcular(() => Ok(servicio().Detalle(Empresa(empresa), inicio, fin, string.IsNullOrWhiteSpace(preparador) ? null : preparador.Trim(),
                filtro, pagina, tamano)));
        }

        /// <summary>Valida y completa el periodo [desde, hasta). Null si vale; si no, el mensaje (de usted).</summary>
        internal static string Periodo(DateTime? desde, DateTime? hasta, DateTime hoy, out DateTime inicio, out DateTime fin)
        {
            fin = (hasta ?? hoy).Date;
            inicio = (desde ?? hoy.AddMonths(-MESES_DESDE_POR_DEFECTO)).Date;
            if (inicio >= fin)
            {
                return "La fecha «desde» tiene que ser anterior a «hasta».";
            }
            if (inicio < fin.AddMonths(-MESES_MAXIMOS))
            {
                return $"El periodo no puede pasar de {MESES_MAXIMOS} meses: acórtelo o pídalo por partes.";
            }
            if (fin > hoy.AddDays(1))
            {
                return "La fecha «hasta» no puede ser posterior a mañana.";
            }
            return null;
        }

        private static string Empresa(string empresa) =>
            string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();

        private IHttpActionResult Calcular(Func<IHttpActionResult> calculo)
        {
            try
            {
                return calculo();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Calidad almacén #914] " + ex.Message, ex));
                return Content(HttpStatusCode.ServiceUnavailable, MENSAJE_NO_SE_PUEDE_LEER);
            }
        }
    }
}
