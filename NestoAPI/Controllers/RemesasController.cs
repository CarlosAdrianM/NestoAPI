using NestoAPI.Infraestructure.Remesas;
using NestoAPI.Models.Remesas;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class RemesasController : ApiController
    {
        private readonly IRemesasService _remesas;

        public RemesasController()
        {
            _remesas = new RemesasService();
        }

        public RemesasController(IRemesasService remesas)
        {
            _remesas = remesas;
        }

        // GET: api/Remesas?empresa=1&top=100
        // Nesto#340 Fase 1C.14 slice 2: remesas de cobro para el grid de RemesasViewModel
        // (top=100 en la carga inicial; sin top = botón "Ver Todas").
        [HttpGet]
        [Route("api/Remesas")]
        [ResponseType(typeof(List<RemesaDTO>))]
        public async Task<IHttpActionResult> GetRemesas(string empresa, int? top = null)
        {
            List<RemesaDTO> remesas = await _remesas.LeerRemesasAsync(empresa, top).ConfigureAwait(false);
            return Ok(remesas);
        }

        // GET: api/Remesas/Movimientos?empresa=1&remesa=10897
        // Nesto#340 Fase 1C.14 slice 3: efectos incluidos en una remesa (grid de la derecha).
        [HttpGet]
        [Route("api/Remesas/Movimientos")]
        [ResponseType(typeof(List<MovimientoRemesaDTO>))]
        public async Task<IHttpActionResult> GetMovimientos(string empresa, int remesa)
        {
            List<MovimientoRemesaDTO> movimientos = await _remesas.LeerMovimientosAsync(empresa, remesa).ConfigureAwait(false);
            return Ok(movimientos);
        }

        // GET: api/Remesas/Impagados?empresa=1&top=100
        // Nesto#340 Fase 1C.14 slice 4: asientos de impagados agrupados (grid izquierdo de la
        // pestaña Impagados; mismo criterio de top que las remesas).
        [HttpGet]
        [Route("api/Remesas/Impagados")]
        [ResponseType(typeof(List<ImpagadoRemesaDTO>))]
        public async Task<IHttpActionResult> GetImpagados(string empresa, int? top = null)
        {
            List<ImpagadoRemesaDTO> impagados = await _remesas.LeerImpagadosAsync(empresa, top).ConfigureAwait(false);
            return Ok(impagados);
        }

        // GET: api/Remesas/EfectosCandidatos?empresa=1
        // NestoAPI#332 (modo simulación): qué efectos entrarían en la remesa SEPA, con
        // preselección, motivo de retención (gating de entrega #172) y flag de clientes con
        // negativos pendientes (puerta de revisión/neteo). NO toca nada.
        [System.Web.Http.Authorize]
        [HttpGet]
        [Route("api/Remesas/EfectosCandidatos")]
        [ResponseType(typeof(List<EfectoCandidatoDTO>))]
        public async Task<IHttpActionResult> GetEfectosCandidatos(string empresa)
        {
            List<EfectoCandidatoDTO> candidatos = await _remesas.LeerEfectosCandidatosSepaAsync(empresa).ConfigureAwait(false);
            return Ok(candidatos);
        }

        // POST: api/Remesas
        // NestoAPI#332 (slices 2-3): crea la remesa de cobros SEPA. Revalida la selección
        // server-side (candidatos frescos, gating #172, puerta de neteo), numera con el
        // contador, da de alta en Remesas y contabiliza el diario _REMESA por el único call
        // site de prdContabilizar. NUNCA escribe en Contabilidad/ExtractoCliente directamente.
        [System.Web.Http.Authorize]
        [HttpPost]
        [Route("api/Remesas")]
        [ResponseType(typeof(CrearRemesaResponse))]
        public async Task<IHttpActionResult> CrearRemesa([FromBody] CrearRemesaRequest peticion)
        {
            string usuario = Infraestructure.UsuarioAuditoriaHelper.Resolver(User, null);
            try
            {
                CrearRemesaResponse respuesta = await _remesas.CrearRemesaAsync(peticion, usuario).ConfigureAwait(false);
                return Ok(respuesta);
            }
            catch (System.ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (System.InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // GET: api/Remesas/Impagados/Movimientos?empresa=1&asiento=1195101
        // Nesto#340 Fase 1C.14 slice 5: movimientos de un asiento de impagados (grid derecho).
        [HttpGet]
        [Route("api/Remesas/Impagados/Movimientos")]
        [ResponseType(typeof(List<MovimientoRemesaDTO>))]
        public async Task<IHttpActionResult> GetMovimientosImpagado(string empresa, int asiento)
        {
            List<MovimientoRemesaDTO> movimientos = await _remesas.LeerMovimientosImpagadoAsync(empresa, asiento).ConfigureAwait(false);
            return Ok(movimientos);
        }
    }
}
