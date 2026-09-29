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

        /// <summary>
        /// NestoAPI#392: POST api/Verifactu/DeclararSimplificada  { "Empresa": "1", "Numero": "NV2615864", "Motivo": "..." }
        /// Declara como simplificada (F2; sus rectificativas R5) una factura completa cuyo NIF no se puede conseguir.
        /// Solo Administración y Dirección, con motivo obligatorio. 400 si no se puede (p. ej. supera el límite legal).
        /// </summary>
        [HttpPost]
        [Route("DeclararSimplificada")]
        [Authorize]
        [ResponseType(typeof(ResultadoReintentoVerifactuDTO))]
        public async Task<IHttpActionResult> DeclararSimplificada([FromBody] DeclararSimplificadaVerifactuDTO dto)
        {
            if (!PuedeDeclararSimplificada(User))
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            if (string.IsNullOrWhiteSpace(dto?.Empresa) || string.IsNullOrWhiteSpace(dto.Numero))
            {
                return BadRequest("Faltan la empresa y el número de la factura");
            }
            if (string.IsNullOrWhiteSpace(dto.Motivo))
            {
                return BadRequest("Hay que indicar el motivo por el que se declara como simplificada (queda registrado).");
            }
            string usuario = Infraestructure.UsuarioAuditoriaHelper.Resolver(User, null);
            ResultadoReintentoVerifactuDTO resultado = await servicio
                .DeclararSimplificada(dto.Empresa, dto.Numero, dto.Motivo, usuario).ConfigureAwait(false);
            if (resultado == null)
            {
                return NotFound();
            }
            if (!resultado.Exitoso)
            {
                return BadRequest(resultado.Mensaje);
            }
            return Ok(resultado);
        }

        /// <summary>NestoAPI#392: decisión de Carlos (29/09/26): solo Administración y Dirección.</summary>
        internal static bool PuedeDeclararSimplificada(IPrincipal usuario)
        {
            return usuario != null
                && (usuario.IsInRoleSinDominio(GruposSeguridad.ADMINISTRACION)
                    || usuario.IsInRoleSinDominio(GruposSeguridad.DIRECCION));
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
