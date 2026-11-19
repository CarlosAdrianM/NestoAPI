using System;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Infraestructure;
using NestoAPI.Infrastructure;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Agencias.RepartoPropio.ModeloTiemposParada;
using NestoAPI.Models;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#881 (corte 3): cómo va el modelo en sombra de los tiempos de parada frente a la regla de constantes. Solo Dirección e
    /// Informática. Solo lectura: el entrenamiento es de noche (job «reparto-tiempos-parada-modelo»), nunca en caliente.
    /// </summary>
    [Authorize]
    public class TiemposRepartoController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO = "Solo Dirección e Informática pueden ver la comparación de los tiempos de parada.";
        public const int ENTRENAMIENTOS_HISTORIA = 60;

        private readonly Func<IRepositorioModeloTiemposParada> repositorio;
        private readonly Func<bool> hayModelo;
        private readonly Func<ConstantesTiemposReparto> constantes;

        public TiemposRepartoController() : this(null, null, null)
        {
        }

        internal TiemposRepartoController(Func<IRepositorioModeloTiemposParada> repositorio, Func<bool> hayModelo, Func<ConstantesTiemposReparto> constantes)
        {
            this.repositorio = repositorio ?? (() => RepositorioModeloTiemposParada.DesdeConfiguracion());
            this.hayModelo = hayModelo ?? (() => AlmacenModeloTiemposParada.Produccion().HayModelo);
            this.constantes = constantes ?? (() => new ConfiguracionTiemposRepartoParametros().Leer());
        }

        internal static bool PuedeVer(IPrincipal usuario)
            => usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION) || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        // GET api/RepartoPropio/TiemposParada/Comparacion
        /// <summary>El último entrenamiento, el del modelo en uso, el veredicto y la historia (para ver cuándo empieza a ganar el modelo).</summary>
        [HttpGet]
        [Route("api/RepartoPropio/TiemposParada/Comparacion")]
        [ResponseType(typeof(ComparacionTiemposParadaDTO))]
        public IHttpActionResult GetComparacion()
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, new { Message = MENSAJE_SIN_PERMISO });
            }
            var dto = new ComparacionTiemposParadaDTO
            {
                QuienManda = constantes().QuienManda,
                ModeloGuardado = SinLanzar(hayModelo)
            };
            IRepositorioModeloTiemposParada repo = repositorio();
            dto.TablasCreadas = repo.TablaEntrenamientosExiste() && repo.TablaTiemposExiste();
            if (!dto.TablasCreadas)
            {
                dto.Veredicto = ModeloTiemposParadaJobsService.MENSAJE_SIN_TABLA;
                return Ok(dto);
            }
            dto.Historia = repo.Ultimos(ENTRENAMIENTOS_HISTORIA);
            dto.UltimoEntrenamiento = dto.Historia.FirstOrDefault();
            dto.ModeloEnUso = repo.UltimoActivo();
            dto.Veredicto = dto.UltimoEntrenamiento?.Veredicto ?? "Todavía no se ha entrenado: el job de las 4:25 apuntará la primera comparación.";
            return Ok(dto);
        }

        private static bool SinLanzar(Func<bool> f)
        {
            try
            {
                return f();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
