using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Models;
using NestoAPI.Models.Traspasos;
using System;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#553: traspasos entre almacenes (reposición de tiendas) fuera de Nesto viejo.
    /// Fase 1, corte a: solo la propuesta, de lectura; no escribe nada.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Traspasos")]
    public class TraspasosController : ApiController
    {
        private readonly IServicioTraspasos servicio;

        public TraspasosController() : this(new ServicioTraspasos())
        {
        }

        public TraspasosController(IServicioTraspasos servicio)
        {
            this.servicio = servicio;
        }

        /// <summary>
        /// Propuesta de reposición de <paramref name="origen"/> a <paramref name="destino"/> calculada
        /// por prdRellenarReposicionStock2 (solo las líneas con CantidadReposicion &gt; 0). 400 si los almacenes no son
        /// válidos. NestoAPI#577: ya no hay 409 por una reposición anterior sin contabilizar en el destino (el
        /// procedimiento nuevo no tiene ese freno; cuenta lo que ya está en camino).
        /// </summary>
        [HttpGet]
        [Route("Propuesta")]
        [ResponseType(typeof(PropuestaTraspasoDTO))]
        public async Task<IHttpActionResult> GetPropuesta(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
            string origen = null, string destino = null)
        {
            try
            {
                PropuestaTraspasoDTO propuesta = await servicio.LeerPropuesta(empresa, origen, destino).ConfigureAwait(false);
                return Ok(propuesta);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
