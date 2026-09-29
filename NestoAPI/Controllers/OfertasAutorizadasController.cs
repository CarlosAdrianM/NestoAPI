using NestoAPI.Infraestructure.OfertasAutorizadas;
using NestoAPI.Models;
using NestoAPI.Models.OfertasCombinadas;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#233: las ofertas autorizadas de cara a los vendedores.
    /// - GET: las vigentes de las tres pestañas en una sola llamada (pantalla de solo lectura de NestoApp#137).
    /// - POST .../InformarVendedores: la push a todos los vendedores de NestoApp. NO se manda sola al guardar
    ///   la oferta: Nesto pregunta tras guardar y solo llama aquí si el usuario dice que sí (Carlos, 29/09/26).
    /// Endpoint nuevo: [Authorize] desde el primer día (Nesto manda JWT; NestoApp, el del vendedor).
    /// </summary>
    [Authorize]
    [RoutePrefix("api/OfertasAutorizadas")]
    public class OfertasAutorizadasController : ApiController
    {
        private readonly IServicioOfertasAutorizadas servicio;

        public OfertasAutorizadasController(IServicioOfertasAutorizadas servicio)
        {
            this.servicio = servicio;
        }

        // GET api/OfertasAutorizadas?empresa=1
        [HttpGet]
        [Route("")]
        [ResponseType(typeof(OfertasAutorizadasDTO))]
        public async Task<IHttpActionResult> GetOfertasAutorizadas(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (string.IsNullOrWhiteSpace(empresa))
            {
                empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            }
            return Ok(await servicio.LeerVigentes(empresa.Trim()).ConfigureAwait(false));
        }

        // POST api/OfertasAutorizadas/combinada/12/InformarVendedores?esNueva=true
        // tipo: combinada | familia | escalonada (en familia, id es el NºOrden)
        [HttpPost]
        [Route("{tipo}/{id:int}/InformarVendedores")]
        [ResponseType(typeof(ResultadoInformarVendedoresDTO))]
        public async Task<IHttpActionResult> InformarVendedores(string tipo, int id, bool esNueva = true)
        {
            if (!servicio.EsTipoValido(tipo))
            {
                return BadRequest($"Tipo de oferta desconocido: '{tipo}'. Debe ser combinada, familia o escalonada");
            }

            try
            {
                return Ok(await servicio.InformarVendedores(tipo, id, esNueva).ConfigureAwait(false));
            }
            catch (OfertaAutorizadaNoEncontradaException)
            {
                return NotFound();
            }
            catch (OfertaNoAvisableException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
