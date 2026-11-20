using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#881 (corte 4): el informe de reparto por repartidor y día (paradas, incidencias, bultos, horas, dinero cobrado, tiempo por parada
    /// real frente al previsto), con totales, medias por día de la semana y desglose por código postal. Solo lectura, de las tablas del corte 3
    /// (RepartoParadasTiempos, sin ella 503) y de las de la furgoneta (#772) y las estimaciones si están. Almacén, Dirección e Informática; con
    /// soloMio=true, el suyo («Mi informe» de Ariadna). Sin km: es el corte 5.
    /// </summary>
    [Authorize]
    public class InformeRepartoController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO = "Solo Almacén, Dirección e Informática pueden ver el informe de reparto.";
        public const string MENSAJE_SIN_TABLA = "Todavía no hay informe de reparto: falta la tabla RepartoParadasTiempos " +
            "(Scripts/Issue881_RepartoFinRutaYTiempos.sql). Avise a Informática.";

        internal Func<IRepositorioInformeReparto> CrearRepositorio { get; set; } = () => RepositorioInformeReparto.DesdeConfiguracion();
        internal Func<DateTime> Ahora { get; set; } = () => DateTime.Now;

        // GET api/RepartoPropio/Informe?desde=2026-11-16&hasta=2026-11-20&repartidor=Miguel&soloMio=false
        [HttpGet]
        [Route("api/RepartoPropio/Informe")]
        [ResponseType(typeof(InformeRepartoDTO))]
        public async Task<IHttpActionResult> GetInforme(DateTime? desde = null, DateTime? hasta = null, string repartidor = null, bool soloMio = false)
        {
            if (!PermisosRepartoPropio.PuedeVerLecturasRechazadas(User))
            {
                return Error(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            DateTime hoy = Ahora().Date;
            DateTime h = (hasta ?? hoy).Date;
            DateTime d = (desde ?? h.AddDays(-6)).Date;
            string motivo = CalculoInformeReparto.MotivoRangoNoValido(d, h);
            if (motivo != null)
            {
                return Error(HttpStatusCode.BadRequest, motivo);
            }
            IRepositorioInformeReparto repositorio = CrearRepositorio();
            if (!repositorio.TablaTiemposExiste())
            {
                return Error(HttpStatusCode.ServiceUnavailable, MENSAJE_SIN_TABLA);
            }
            string quien = soloMio ? (string.IsNullOrWhiteSpace(User?.Identity?.Name) ? "(nadie)" : User.Identity.Name) : repartidor;
            DatosInformeReparto datos = await repositorio.Leer(d, h).ConfigureAwait(true);
            InformeRepartoDTO informe = CalculoInformeReparto.Calcular(d, h, datos, quien, Ahora());
            informe.Aviso = CalculoInformeReparto.Aviso(datos);
            return Ok(informe);
        }

        private IHttpActionResult Error(HttpStatusCode codigo, string mensaje)
            => Request != null ? ResponseMessage(Request.CreateErrorResponse(codigo, mensaje)) : (IHttpActionResult)Content(codigo, mensaje);
    }
}
