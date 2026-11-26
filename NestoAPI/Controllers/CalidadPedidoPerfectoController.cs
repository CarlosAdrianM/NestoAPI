using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Calidad.PedidoPerfecto;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#914 (corte 1): el KPI «pedido perfecto», SOLO LECTURA y en sombra (Dirección e Informática). Sin nombres de personas en el
    /// resumen; el detalle por pedido sí lleva vendedor y usuario. Definiciones en docs/claude/calidad-kpi.md.
    /// </summary>
    [Authorize]
    public class CalidadPedidoPerfectoController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO = "Solo Dirección o Informática pueden ver el indicador del pedido perfecto.";
        public const string MENSAJE_NO_SE_PUEDE_LEER = "No se ha podido calcular el pedido perfecto ahora mismo. Inténtelo de nuevo en unos minutos y, si sigue, avise a Informática.";
        /// <summary>Por defecto, hasta hace 21 días (para que hayan tenido tiempo de servirse y cobrarse) y desde 12 meses antes de hoy.</summary>
        public const int DIAS_HASTA_POR_DEFECTO = 21;
        public const int MESES_DESDE_POR_DEFECTO = 12;
        public const int MESES_MAXIMOS = 13;
        public const int TAMANO_POR_DEFECTO = 100;
        public const int TAMANO_MAXIMO = 1000;

        private readonly Func<IServicioPedidoPerfecto> servicio;
        private readonly Func<DateTime> hoyMadrid;

        public CalidadPedidoPerfectoController() : this(null)
        {
        }

        internal CalidadPedidoPerfectoController(Func<IServicioPedidoPerfecto> servicio, Func<DateTime> hoyMadrid = null)
        {
            this.servicio = servicio ?? (() => new ServicioPedidoPerfecto(ahora: () => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Startup.ZonaHorariaPeninsular())));
            this.hoyMadrid = hoyMadrid ?? (() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Startup.ZonaHorariaPeninsular()).Date);
        }

        internal static bool PuedeVer(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        // GET api/Calidad/PedidoPerfecto/Resumen?empresa=1&desde=2025-11-26&hasta=2026-11-05
        [HttpGet]
        [Route("api/Calidad/PedidoPerfecto/Resumen")]
        [ResponseType(typeof(ResumenPedidoPerfectoDTO))]
        public IHttpActionResult GetResumen(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, DateTime? desde = null, DateTime? hasta = null)
        {
            if (!PuedeVer(User))
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

        // GET api/Calidad/PedidoPerfecto/Pedidos?empresa=1&desde=&hasta=&motivo=NoSalioAlDiaSiguiente&pagina=1&tamano=100
        [HttpGet]
        [Route("api/Calidad/PedidoPerfecto/Pedidos")]
        [ResponseType(typeof(PaginaPedidosPerfectoDTO))]
        public IHttpActionResult GetPedidos(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, DateTime? desde = null, DateTime? hasta = null,
            string motivo = null, int pagina = 1, int tamano = TAMANO_POR_DEFECTO)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            string error = Periodo(desde, hasta, hoyMadrid(), out DateTime inicio, out DateTime fin);
            if (error != null)
            {
                return BadRequest(error);
            }
            MotivoPedidoPerfecto? filtro = null;
            if (!string.IsNullOrWhiteSpace(motivo))
            {
                if (!Enum.TryParse(motivo.Trim(), true, out MotivoPedidoPerfecto leido) || !Enum.IsDefined(typeof(MotivoPedidoPerfecto), leido)
                    || int.TryParse(motivo.Trim(), out _))
                {
                    return BadRequest($"No existe el motivo «{motivo.Trim()}». Los motivos son: " +
                        string.Join(", ", InfoMotivosPedidoPerfecto.Todos.Select(m => m.ToString())) + ".");
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
            return Calcular(() => Ok(servicio().Pedidos(Empresa(empresa), inicio, fin, filtro, pagina, tamano)));
        }

        /// <summary>Valida y completa el periodo [desde, hasta). Null si vale; si no, el mensaje (de usted).</summary>
        internal static string Periodo(DateTime? desde, DateTime? hasta, DateTime hoy, out DateTime inicio, out DateTime fin)
        {
            fin = (hasta ?? hoy.AddDays(-DIAS_HASTA_POR_DEFECTO)).Date;
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
                ElmahHelper.Log(new Exception("[Pedido perfecto #914] " + ex.Message, ex));
                return Content(HttpStatusCode.ServiceUnavailable, MENSAJE_NO_SE_PUEDE_LEER);
            }
        }
    }
}
