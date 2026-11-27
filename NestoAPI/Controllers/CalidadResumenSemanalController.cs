using NestoAPI.Infraestructure.Calidad.ResumenSemanal;
using NestoAPI.Infraestructure.Calidad.Tablero;
using System;
using System.Net;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#914: el correo semanal de calidad sin esperar al lunes (Dirección e Informática). Con ensayo (por defecto) NO manda nada y
    /// devuelve el asunto y el HTML; sin ensayo lo manda SOLO al destinatario configurado (ParametrosUsuario «(defecto)»
    /// Calidad.CorreoResumenSemanal): la petición no admite otro destinatario. Definiciones en docs/claude/calidad-kpi.md.
    /// </summary>
    [Authorize]
    public class CalidadResumenSemanalController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO = "Solo Dirección o Informática pueden ver o mandar el correo semanal de calidad.";
        public const int SEMANAS_MAXIMAS_ATRAS = 104;

        private readonly Func<ServicioCorreoResumenSemanal> servicio;
        private readonly Func<DateTime> hoyMadrid;

        public CalidadResumenSemanalController() : this(null)
        {
        }

        internal CalidadResumenSemanalController(Func<ServicioCorreoResumenSemanal> servicio, Func<DateTime> hoyMadrid = null)
        {
            this.servicio = servicio ?? (() => new ServicioCorreoResumenSemanal());
            this.hoyMadrid = hoyMadrid ?? (() => FuentesKpiSql.AhoraMadrid().Date);
        }

        // POST api/Calidad/ResumenSemanal/Enviar?semana=2026-11-23&ensayo=true
        [HttpPost]
        [Route("api/Calidad/ResumenSemanal/Enviar")]
        [ResponseType(typeof(ResultadoCorreoResumenSemanalDTO))]
        public IHttpActionResult PostEnviar(DateTime? semana = null, bool ensayo = true)
        {
            if (!CalidadPedidoPerfectoController.PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            string error = Semana(semana, hoyMadrid(), out DateTime lunes);
            if (error != null)
            {
                return BadRequest(error);
            }
            ResultadoCorreoResumenSemanalDTO r = servicio().Ejecutar(lunes, ensayo, User?.Identity?.Name);
            switch (r.EstadoEnum)
            {
                case EstadoCorreoResumenSemanal.Ensayo:
                case EstadoCorreoResumenSemanal.Enviado:
                    return Ok(r);
                case EstadoCorreoResumenSemanal.YaEnviado:
                case EstadoCorreoResumenSemanal.SinDestinatario:
                    return Content(HttpStatusCode.Conflict, r);
                default:
                    return Content(HttpStatusCode.ServiceUnavailable, r);
            }
        }

        /// <summary>El lunes de la semana pedida (sin ella, la última cerrada). Tiene que estar cerrada y no ser de hace más de 104 semanas.</summary>
        internal static string Semana(DateTime? semana, DateTime hoy, out DateTime lunes)
        {
            lunes = semana.HasValue ? SemanaKpi.LunesDe(semana.Value) : ReglaCorreoResumenSemanal.SemanaCerrada(hoy);
            if (lunes.AddDays(7) > hoy.Date)
            {
                return "Esa semana todavía no ha acabado: el correo es de una semana cerrada (de lunes a domingo).";
            }
            if (lunes < SemanaKpi.LunesDe(hoy).AddDays(-7 * SEMANAS_MAXIMAS_ATRAS))
            {
                return $"La semana no puede ser de hace más de {SEMANAS_MAXIMAS_ATRAS} semanas.";
            }
            return null;
        }
    }
}
