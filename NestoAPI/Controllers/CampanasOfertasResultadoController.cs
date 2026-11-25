using NestoAPI.Infraestructure.Campanas.OfertasParaTi;
using System;
using System.Collections.Generic;
using System.Net;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// «Ofertas para ti», en sombra: qué ha pasado con lo enseñado (servidas, vistas, abiertas, añadidas y compradas) por origen, por motivo y
    /// por decil de probabilidad del modelo, para ver si el modelo ordena bien. Solo lectura; Dirección e Informática (403 de usted); 503 sin
    /// Scripts/OfertasClienteMostradas.sql.
    /// </summary>
    [Authorize]
    public class CampanasOfertasResultadoController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO = "El resultado de las ofertas solo lo pueden ver Dirección e Informática.";
        public const string MENSAJE_SIN_TABLA = "Falta dbo.OfertasClienteMostradas (Scripts/OfertasClienteMostradas.sql): todavía no se registra lo que se enseña.";

        private readonly Func<IRepositorioOfertasMostradas> repositorio;

        public CampanasOfertasResultadoController() : this(null)
        {
        }

        internal CampanasOfertasResultadoController(Func<IRepositorioOfertasMostradas> repositorio)
        {
            this.repositorio = repositorio ?? RepositorioOfertasMostradasSql.DesdeConfiguracion;
        }

        /// <summary>Hoy (sustituible en las pruebas).</summary>
        internal Func<DateTime> Hoy { get; set; } = () => DateTime.Today;

        // GET api/Campanas/Ofertas/Resultado?campana=Black%20Friday%202026&dias=30
        [HttpGet]
        [Route("api/Campanas/Ofertas/Resultado")]
        [ResponseType(typeof(ResultadoOfertasMostradasDTO))]
        public IHttpActionResult GetResultado(string campana = null, int? dias = null)
        {
            if (!ReglaPushCampana.PuedeLanzar(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            int n = Math.Max(1, Math.Min(ReglaResultadoOfertas.DIAS_TOPE, dias ?? ReglaResultadoOfertas.DIAS_POR_DEFECTO));
            IRepositorioOfertasMostradas repo = repositorio();
            if (!repo.TablaExiste())
            {
                return Content(HttpStatusCode.ServiceUnavailable, MENSAJE_SIN_TABLA);
            }
            DateTime desde = Hoy().Date.AddDays(-(n - 1));
            string laCampana = string.IsNullOrWhiteSpace(campana) ? null : campana.Trim();
            List<FilaResultadoOferta> filas = repo.LeerResultado(laCampana, desde);
            return Ok(ReglaResultadoOfertas.Resumir(filas, laCampana, n, desde));
        }
    }
}
