using NestoAPI.Infrastructure;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models.Facturas;
using System.Collections.Generic;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using static NestoAPI.Models.Constantes;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#522: ventana de administración de Nesto con las facturas que Verifactu todavía no da por buenas
    /// (sin registrar o incorrectas en la AEAT), su motivo y el reintento del envío una vez corregidas.
    /// Solo Administración, Dirección e Informática (el JWT de Nesto lleva los grupos del dominio).
    /// </summary>
    [RoutePrefix("api/Verifactu")]
    public class VerifactuController : ApiController
    {
        private readonly IServicioFacturasPendientesVerifactu servicio;

        public VerifactuController(IServicioFacturasPendientesVerifactu servicio)
        {
            this.servicio = servicio;
        }

        /// <summary>GET api/Verifactu/FacturasPendientes</summary>
        [HttpGet]
        [Route("FacturasPendientes")]
        [Authorize]
        [ResponseType(typeof(List<FacturaPendienteVerifactuDTO>))]
        public async Task<IHttpActionResult> GetFacturasPendientes()
        {
            if (!PuedeGestionar(User))
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            return Ok(await servicio.Listar().ConfigureAwait(false));
        }

        /// <summary>POST api/Verifactu/ReintentarFactura  { "Empresa": "1", "Numero": "NV2615864" }</summary>
        [HttpPost]
        [Route("ReintentarFactura")]
        [Authorize]
        [ResponseType(typeof(ResultadoReintentoVerifactuDTO))]
        public async Task<IHttpActionResult> ReintentarFactura([FromBody] ReintentarFacturaVerifactuDTO dto)
        {
            if (!PuedeGestionar(User))
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            if (string.IsNullOrWhiteSpace(dto?.Empresa) || string.IsNullOrWhiteSpace(dto.Numero))
            {
                return BadRequest("Faltan la empresa y el número de la factura");
            }
            ResultadoReintentoVerifactuDTO resultado = await servicio.Reintentar(dto.Empresa, dto.Numero).ConfigureAwait(false);
            if (resultado == null)
            {
                return NotFound();
            }
            return Ok(resultado);
        }

        internal static bool PuedeGestionar(IPrincipal usuario)
        {
            return usuario != null
                && (usuario.IsInRoleSinDominio(GruposSeguridad.ADMINISTRACION)
                    || usuario.IsInRoleSinDominio(GruposSeguridad.DIRECCION)
                    || usuario.IsInRoleSinDominio(NovedadesController.GRUPO_INFORMATICA));
        }
    }
}
