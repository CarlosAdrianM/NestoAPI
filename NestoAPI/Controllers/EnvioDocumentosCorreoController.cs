using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.EnvioDocumentosCorreo;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#724 (sugerencia 831, María José): mandar al cliente por correo el PDF del pedido o del mandato SEPA sin
    /// descargarlo (Nesto corre en el escritorio remoto y Outlook web no adjunta ficheros de allí). Solo empleados,
    /// como el envío de facturas (<see cref="FacturasController.MOTIVO_SOLO_EMPLEADOS_CORREO"/>).
    /// Aparte de PedidosVentaController y ClientesController a propósito, para no engordarlos más.
    /// </summary>
    [Authorize]
    public class EnvioDocumentosCorreoController : ApiController
    {
        internal const string MOTIVO_SOLO_EMPLEADOS = "Solo los empleados pueden enviar documentos por correo.";

        private readonly GestorEnvioDocumentosCorreo gestor;
        private readonly IProveedorDocumentosCorreo proveedor;

        public EnvioDocumentosCorreoController() : this(new ProveedorDocumentosCorreo(), new ServicioCorreoElectronico())
        {
        }

        // Para los tests (internal: el contenedor solo ve el constructor público sin parámetros)
        internal EnvioDocumentosCorreoController(IProveedorDocumentosCorreo proveedor, IServicioCorreoElectronico servicioCorreo)
        {
            this.proveedor = proveedor;
            gestor = new GestorEnvioDocumentosCorreo(proveedor, servicioCorreo);
        }

        private bool EsEmpleado()
            => (User?.Identity as ClaimsIdentity)?.FindFirst("IsEmployee")?.Value == "true";

        private static string EmpresaOPorDefecto(string empresa)
            => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();

        // POST api/PedidosVenta/1/929498/EnviarPorCorreo   { "Destinatarios": ["cliente@correo.es"], "Asunto": "...", "Texto": "..." }
        [HttpPost]
        [Route("api/PedidosVenta/{empresa}/{numero:int}/EnviarPorCorreo")]
        [ResponseType(typeof(ResultadoEnvioDocumentoCorreoDTO))]
        public Task<IHttpActionResult> EnviarPedidoPorCorreo(string empresa, int numero, [FromBody] EnvioDocumentoCorreoDTO envio)
            => Ejecutar(() => gestor.EnviarPedido(EmpresaOPorDefecto(empresa), numero, envio ?? new EnvioDocumentoCorreoDTO(), User?.Identity?.Name));

        // POST api/Clientes/1/15191/0/Mandato/EnviarPorCorreo   { "Ccc": "1", "Motor": "QuestPDF", "Destinatarios": [...] }
        [HttpPost]
        [Route("api/Clientes/{empresa}/{cliente}/{contacto}/Mandato/EnviarPorCorreo")]
        [ResponseType(typeof(ResultadoEnvioDocumentoCorreoDTO))]
        public Task<IHttpActionResult> EnviarMandatoPorCorreo(string empresa, string cliente, string contacto, [FromBody] EnvioMandatoCorreoDTO envio)
            => Ejecutar(() => gestor.EnviarMandato(EmpresaOPorDefecto(empresa), cliente?.Trim(), contacto?.Trim(),
                envio ?? new EnvioMandatoCorreoDTO(), User?.Identity?.Name));

        // GET api/Clientes/1/15191/0/CorreosContacto
        /// <summary>NestoAPI#724: los correos de las personas de contacto, para proponerlos en el diálogo de envío de Nesto.</summary>
        [HttpGet]
        [Route("api/Clientes/{empresa}/{cliente}/{contacto}/CorreosContacto")]
        [ResponseType(typeof(List<string>))]
        public async Task<IHttpActionResult> GetCorreosContacto(string empresa, string cliente, string contacto)
        {
            if (!EsEmpleado())
            {
                return Content(HttpStatusCode.Forbidden, MOTIVO_SOLO_EMPLEADOS);
            }
            List<string> correos = await proveedor.CorreosContacto(EmpresaOPorDefecto(empresa), cliente?.Trim(), contacto?.Trim()).ConfigureAwait(false);
            return Ok(correos ?? new List<string>());
        }

        private async Task<IHttpActionResult> Ejecutar(System.Func<Task<ResultadoEnvioDocumentoCorreoDTO>> envio)
        {
            if (!EsEmpleado())
            {
                return Content(HttpStatusCode.Forbidden, MOTIVO_SOLO_EMPLEADOS);
            }
            try
            {
                return Ok(await envio().ConfigureAwait(false));
            }
            catch (NestoBusinessException ex)
            {
                return ex.StatusCode == HttpStatusCode.BadRequest
                    ? BadRequest(ex.Message)
                    : (IHttpActionResult)Content(ex.StatusCode, ex.Message);
            }
        }
    }
}
