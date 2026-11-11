using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.EnvioDocumentosCorreo;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using System;
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
    /// descargarlo (Nesto corre en el escritorio remoto y Outlook web no adjunta ficheros de allí). Empleados, y los vendedores
    /// de NestoApp solo para sus clientes (06/11/26, <see cref="ClientesDelVendedor"/>), como el envío de facturas (<see cref="FacturasController.MOTIVO_SOLO_EMPLEADOS_CORREO"/>).
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

        /// <summary>Vendedores de NestoApp (06/11/26): (empresa, usuario, vendedor, cliente) → si el cliente es suyo. Sustituible en los tests.</summary>
        internal Func<string, string, string, string, bool> EsClienteDelVendedor { get; set; } = ClientesDelVendedor.EsDelVendedor;

        /// <summary>
        /// Empleados, todo; vendedores de NestoApp (06/11/26), solo sus clientes; los demás, 403. Null si puede seguir. Con
        /// <paramref name="cliente"/> null (el pedido: el cliente sale del documento), el vendedor pasa y lo comprueba el gestor
        /// con <see cref="ClientePermitido"/>.
        /// </summary>
        private IHttpActionResult Rechazo(string empresa, string cliente)
        {
            if (EsEmpleado())
            {
                return null;
            }
            var identity = User?.Identity as ClaimsIdentity;
            if (!ClientesDelVendedor.EsVendedorDeNestoApp(identity))
            {
                return Content(HttpStatusCode.Forbidden, MOTIVO_SOLO_EMPLEADOS);
            }
            if (cliente == null || ClientesDelVendedor.PuedeElVendedor(identity, empresa, cliente, EsClienteDelVendedor))
            {
                return null;
            }
            return Content(HttpStatusCode.Forbidden, ClientesDelVendedor.MOTIVO_SOLO_SUS_CLIENTES);
        }

        /// <summary>Para el gestor: null si es empleado (sin comprobar); si es vendedor, (empresa, cliente) → si es suyo.</summary>
        private Func<string, string, bool> ClientePermitido()
        {
            if (EsEmpleado())
            {
                return null;
            }
            var identity = User?.Identity as ClaimsIdentity;
            return (empresa, cliente) => ClientesDelVendedor.PuedeElVendedor(identity, empresa, cliente, EsClienteDelVendedor);
        }

        /// <summary>Sugerencia 1167: el vendedor del token de NestoApp (claim «Vendedor»); null en Nesto.</summary>
        private string VendedorDelToken() => (User?.Identity as ClaimsIdentity)?.FindFirst("Vendedor")?.Value?.Trim();

        private static string EmpresaOPorDefecto(string empresa)
            => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();

        // POST api/PedidosVenta/1/929498/EnviarPorCorreo   { "Destinatarios": ["cliente@correo.es"], "Asunto": "...", "Texto": "..." }
        [HttpPost]
        [Route("api/PedidosVenta/{empresa}/{numero:int}/EnviarPorCorreo")]
        [ResponseType(typeof(ResultadoEnvioDocumentoCorreoDTO))]
        public Task<IHttpActionResult> EnviarPedidoPorCorreo(string empresa, int numero, [FromBody] EnvioDocumentoCorreoDTO envio)
            => Ejecutar(EmpresaOPorDefecto(empresa), null, () => gestor.EnviarPedido(EmpresaOPorDefecto(empresa), numero,
                envio ?? new EnvioDocumentoCorreoDTO(), User?.Identity?.Name, ClientePermitido(), VendedorDelToken()));

        // POST api/Clientes/1/15191/0/Mandato/EnviarPorCorreo   { "Ccc": "1", "Motor": "QuestPDF", "Destinatarios": [...] }
        [HttpPost]
        [Route("api/Clientes/{empresa}/{cliente}/{contacto}/Mandato/EnviarPorCorreo")]
        [ResponseType(typeof(ResultadoEnvioDocumentoCorreoDTO))]
        public Task<IHttpActionResult> EnviarMandatoPorCorreo(string empresa, string cliente, string contacto, [FromBody] EnvioMandatoCorreoDTO envio)
            => Ejecutar(EmpresaOPorDefecto(empresa), cliente?.Trim() ?? string.Empty, () => gestor.EnviarMandato(EmpresaOPorDefecto(empresa), cliente?.Trim(), contacto?.Trim(),
                envio ?? new EnvioMandatoCorreoDTO(), User?.Identity?.Name, VendedorDelToken()));

        // GET api/Clientes/1/15191/0/CorreosContacto
        /// <summary>NestoAPI#724: los correos de las personas de contacto, para proponerlos en el diálogo de envío de Nesto.</summary>
        [HttpGet]
        [Route("api/Clientes/{empresa}/{cliente}/{contacto}/CorreosContacto")]
        [ResponseType(typeof(List<string>))]
        public async Task<IHttpActionResult> GetCorreosContacto(string empresa, string cliente, string contacto)
        {
            IHttpActionResult rechazo = Rechazo(EmpresaOPorDefecto(empresa), cliente?.Trim() ?? string.Empty);
            if (rechazo != null)
            {
                return rechazo;
            }
            List<string> correos = await proveedor.CorreosContacto(EmpresaOPorDefecto(empresa), cliente?.Trim(), contacto?.Trim()).ConfigureAwait(false);
            return Ok(correos ?? new List<string>());
        }

        private async Task<IHttpActionResult> Ejecutar(string empresa, string cliente, Func<Task<ResultadoEnvioDocumentoCorreoDTO>> envio)
        {
            IHttpActionResult rechazo = Rechazo(empresa, cliente);
            if (rechazo != null)
            {
                return rechazo;
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
