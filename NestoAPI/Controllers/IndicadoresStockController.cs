using Hangfire;
using NestoAPI.Infrastructure;
using NestoAPI.Infraestructure.IndicadoresStock;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using static NestoAPI.Models.Constantes;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#629 (corte 1 de la épica #628, control de stock obsoleto): los indicadores mensuales de stock por almacén y TOTAL
    /// (tabla IndicadoresStock, que rellena de noche <see cref="IndicadoresStockJobsService"/>). El GET solo lee esa tabla; los
    /// cálculos pesados nunca se lanzan desde aquí: la reconstrucción se encola y el job trabaja de noche.
    /// </summary>
    [RoutePrefix("api/IndicadoresStock")]
    public class IndicadoresStockController : ApiController
    {
        private readonly NVEntities db;
        private readonly Func<IRepositorioIndicadoresStock> repositorio;
        private readonly Func<int, DateTime, string> encolar;
        private readonly Func<DateTime> ahora;

        public IndicadoresStockController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            repositorio = () => new RepositorioIndicadoresStockSql(db, RepositorioIndicadoresStockSql.SEGUNDOS_CONSULTA_ENDPOINT);
            encolar = (meses, desde) => BackgroundJob.Enqueue(() => IndicadoresStockJobsService.Reconstruir(meses, desde));
            ahora = () => DateTime.Now;
        }

        /// <param name="encolar">(meses, desde) → id del job de Hangfire (sustituible en tests).</param>
        internal IndicadoresStockController(Func<IRepositorioIndicadoresStock> repositorio, Func<int, DateTime, string> encolar = null,
            Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio;
            this.encolar = encolar ?? ((m, d) => "0");
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        /// <summary>
        /// Las filas de IndicadoresStock de los meses <paramref name="desde"/> a <paramref name="hasta"/> (AAAA-MM, incluidos; por
        /// defecto los 36 meses que acaban en el mes anterior), de un almacén (ALG, REI, ALC…, TOTAL; NestoAPI#628: o una agrupación
        /// por tipo de exclusiva, G:PRP, G:NAC, G:MAD, G:NIG, G:EXT, G:SIN, que salen detrás de los almacenes) o de todos. Cada fila lleva
        /// además el % de unidades compradas en sus 12 meses con más de 6 meses de cobertura. Dirección e Informática.
        /// GET api/IndicadoresStock?desde=2023-10&amp;hasta=2026-09&amp;almacen=TOTAL
        /// </summary>
        [HttpGet]
        [Route("")]
        [Authorize]
        public async Task<IHttpActionResult> GetIndicadoresStock(string desde = null, string hasta = null, string almacen = null)
        {
            if (!PuedeVer())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            DateTime mesHasta = IndicadoresStockJobsService.MesAnterior(ahora());
            if (!string.IsNullOrWhiteSpace(hasta) && !TryLeerMes(hasta, out mesHasta))
            {
                return BadRequest($"«{hasta}» no es un mes: tiene que ser AAAA-MM.");
            }
            DateTime mesDesde = mesHasta.AddMonths(-(IndicadoresStockJobsService.MESES_RECONSTRUCCION - 1));
            if (!string.IsNullOrWhiteSpace(desde) && !TryLeerMes(desde, out mesDesde))
            {
                return BadRequest($"«{desde}» no es un mes: tiene que ser AAAA-MM.");
            }
            if (mesDesde > mesHasta)
            {
                return BadRequest("«desde» no puede ser posterior a «hasta».");
            }

            IRepositorioIndicadoresStock repo = repositorio();
            if (!await repo.ExisteTabla().ConfigureAwait(false))
            {
                return Content(HttpStatusCode.ServiceUnavailable, new { Message = "Falta la tabla IndicadoresStock (Scripts/Issue629_IndicadoresStock.sql)." });
            }
            // 11 meses más para poder calcular el % de compras de 12 meses de la primera fila
            List<FilaIndicadoresStock> filas = await repo.Leer(mesDesde.AddMonths(-11), mesHasta, almacen).ConfigureAwait(false);
            return Ok(ConstruirRespuesta(filas, mesDesde, mesHasta));
        }

        /// <summary>
        /// Encola la reconstrucción de <paramref name="meses"/> meses (por defecto 36) desde <paramref name="desde"/> (AAAA-MM; por
        /// defecto el mes anterior) hacia atrás, el más reciente primero. El job solo trabaja de noche: si se encola de día, empieza
        /// a las 22:00. Cada mes se borra y se vuelve a calcular, así que se puede relanzar sin duplicar (meses=1 recalcula un mes
        /// concreto). Solo Dirección.
        /// POST api/IndicadoresStock/Reconstruir?meses=36
        /// </summary>
        [HttpPost]
        [Route("Reconstruir")]
        [Authorize]
        public IHttpActionResult Reconstruir(int meses = IndicadoresStockJobsService.MESES_RECONSTRUCCION, string desde = null)
        {
            if (User == null || !User.IsInRoleSinDominio(GruposSeguridad.DIRECCION))
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            if (meses < 1 || meses > IndicadoresStockJobsService.MAXIMO_MESES_RECONSTRUCCION)
            {
                return BadRequest($"«meses» tiene que estar entre 1 y {IndicadoresStockJobsService.MAXIMO_MESES_RECONSTRUCCION}.");
            }
            DateTime momento = ahora();
            DateTime mesDesde = IndicadoresStockJobsService.MesAnterior(momento);
            if (!string.IsNullOrWhiteSpace(desde) && !TryLeerMes(desde, out mesDesde))
            {
                return BadRequest($"«{desde}» no es un mes: tiene que ser AAAA-MM.");
            }
            if (mesDesde > IndicadoresStockJobsService.MesAnterior(momento))
            {
                return BadRequest("Solo se pueden calcular meses cerrados (hasta el mes anterior).");
            }

            string jobId;
            try
            {
                jobId = encolar(meses, mesDesde);
            }
            catch (InvalidOperationException ex)
            {
                // Sin Hangfire (en DEBUG no se arranca) no hay dónde encolarlo.
                return Content(HttpStatusCode.ServiceUnavailable, new { Message = "No se puede encolar la reconstrucción: " + ex.Message });
            }
            DateTime hastaMes = mesDesde.AddMonths(-(meses - 1));
            string cuando = IndicadoresStockJobsService.EsDeNoche(momento)
                ? "empieza ya"
                : $"empezará a las {IndicadoresStockJobsService.HORA_INICIO_NOCHE}:00 (solo trabaja de noche)";
            return Content(HttpStatusCode.Accepted, new
            {
                JobId = jobId,
                Meses = meses,
                Desde = mesDesde.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                Hasta = hastaMes.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                Mensaje = $"Reconstrucción encolada de {mesDesde:yyyy-MM} hacia atrás hasta {hastaMes:yyyy-MM}, un mes cada vez; {cuando}."
            });
        }

        /// <summary>Las filas de [desde, hasta] con el % de compras de 12 meses calculado con las filas de su almacén.</summary>
        internal static List<IndicadoresStockDTO> ConstruirRespuesta(IEnumerable<FilaIndicadoresStock> filas, DateTime desde, DateTime hasta)
        {
            var respuesta = new List<IndicadoresStockDTO>();
            foreach (IGrouping<string, FilaIndicadoresStock> deAlmacen in filas.GroupBy(f => f.Almacen?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                List<FilaIndicadoresStock> lista = deAlmacen.ToList();
                foreach (FilaIndicadoresStock fila in lista.Where(f => f.Mes >= desde && f.Mes <= hasta))
                {
                    IndicadoresStockDTO dto = Copiar(fila);
                    dto.Periodo = fila.Mes.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                    dto.PorcentajeComprasCoberturaAlta12Meses = CalculadoraIndicadoresStock.CalcularPorcentajeCompras12Meses(lista, fila.Mes);
                    respuesta.Add(dto);
                }
            }
            return respuesta
                .OrderBy(f => f.Mes)
                .ThenBy(f => f.Almacen == FilaIndicadoresStock.TOTAL ? 0 : AgrupacionesStock.EsAgrupacion(f.Almacen) ? 2 : 1)
                .ThenBy(f => f.Almacen, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IndicadoresStockDTO Copiar(FilaIndicadoresStock f)
        {
            var dto = new IndicadoresStockDTO();
            foreach (System.Reflection.PropertyInfo propiedad in typeof(FilaIndicadoresStock).GetProperties())
            {
                if (propiedad.CanWrite)
                {
                    propiedad.SetValue(dto, propiedad.GetValue(f));
                }
            }
            dto.Almacen = f.Almacen?.Trim();
            return dto;
        }

        internal static bool TryLeerMes(string texto, out DateTime mes)
        {
            return DateTime.TryParseExact(texto.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out mes);
        }

        private bool PuedeVer()
        {
            return User != null && (User.IsInRoleSinDominio(GruposSeguridad.DIRECCION) || User.IsInRoleSinDominio(NovedadesController.GRUPO_INFORMATICA));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
