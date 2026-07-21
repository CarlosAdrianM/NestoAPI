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
