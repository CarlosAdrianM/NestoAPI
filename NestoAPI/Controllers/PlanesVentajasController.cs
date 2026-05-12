using NestoAPI.Infraestructure.PlanesVentajas;
using NestoAPI.Models.PlanesVentajas;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    [Authorize]
    public class PlanesVentajasController : ApiController
    {
        private readonly IPlanesVentajasService _servicio;

        public PlanesVentajasController(IPlanesVentajasService servicio)
        {
            _servicio = servicio;
        }

        // GET: api/PlanesVentajas/Estados
        [HttpGet]
        [Route("api/PlanesVentajas/Estados")]
        [ResponseType(typeof(List<EstadoPlanVentajasDTO>))]
        public async Task<IHttpActionResult> GetEstados()
        {
            var estados = await _servicio.ListarEstadosAsync().ConfigureAwait(false);
            return Ok(estados);
        }

        // GET: api/PlanesVentajas/Empresas
        [HttpGet]
        [Route("api/PlanesVentajas/Empresas")]
        [ResponseType(typeof(List<EmpresaResumenDTO>))]
        public async Task<IHttpActionResult> GetEmpresas()
        {
            var empresas = await _servicio.ListarEmpresasAsync().ConfigureAwait(false);
            return Ok(empresas);
        }

        // GET: api/PlanesVentajas?vendedor=&filtroCliente=&incluirCancelados=
        [HttpGet]
        [Route("api/PlanesVentajas")]
        [ResponseType(typeof(List<PlanVentajasDTO>))]
        public async Task<IHttpActionResult> GetPlanes(string vendedor = null, string filtroCliente = null, bool incluirCancelados = false)
        {
            var planes = await _servicio.ListarPlanesAsync(vendedor, filtroCliente, incluirCancelados).ConfigureAwait(false);
            return Ok(planes);
        }

        // GET: api/PlanesVentajas/{numero}
        [HttpGet]
        [Route("api/PlanesVentajas/{numero:int}")]
        [ResponseType(typeof(PlanVentajasDTO))]
        public async Task<IHttpActionResult> GetPlan(int numero)
        {
            var plan = await _servicio.ObtenerPlanAsync(numero).ConfigureAwait(false);
            if (plan == null)
            {
                return NotFound();
            }
            return Ok(plan);
        }

        // GET: api/PlanesVentajas/{numero}/Clientes
        [HttpGet]
        [Route("api/PlanesVentajas/{numero:int}/Clientes")]
        [ResponseType(typeof(List<ClientePlanVentajasDTO>))]
        public async Task<IHttpActionResult> GetClientes(int numero, string empresa = null)
        {
            var clientes = await _servicio.ObtenerClientesAsync(numero, empresa).ConfigureAwait(false);
            return Ok(clientes);
        }

        // GET: api/PlanesVentajas/{numero}/LineasVenta
        [HttpGet]
        [Route("api/PlanesVentajas/{numero:int}/LineasVenta")]
        [ResponseType(typeof(List<LineaVentaPlanDTO>))]
        public async Task<IHttpActionResult> GetLineasVenta(int numero, string empresa = null)
        {
            var lineas = await _servicio.ObtenerLineasVentaAsync(numero, empresa).ConfigureAwait(false);
            return Ok(lineas);
        }

        // POST: api/PlanesVentajas
        [HttpPost]
        [Route("api/PlanesVentajas")]
        [ResponseType(typeof(PlanVentajasDTO))]
        public async Task<IHttpActionResult> PostPlan(PlanVentajasDTO plan)
        {
            if (plan == null || !ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            string usuario = User?.Identity?.Name;
            var creado = await _servicio.CrearPlanAsync(plan, usuario).ConfigureAwait(false);
            return Ok(creado);
        }

        // PUT: api/PlanesVentajas/{numero}
        [HttpPut]
        [Route("api/PlanesVentajas/{numero:int}")]
        [ResponseType(typeof(PlanVentajasDTO))]
        public async Task<IHttpActionResult> PutPlan(int numero, PlanVentajasDTO plan)
        {
            if (plan == null || !ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            string usuario = User?.Identity?.Name;
            var actualizado = await _servicio.ActualizarPlanAsync(numero, plan, usuario).ConfigureAwait(false);
            if (actualizado == null)
            {
                return NotFound();
            }
            return Ok(actualizado);
        }
    }
}
