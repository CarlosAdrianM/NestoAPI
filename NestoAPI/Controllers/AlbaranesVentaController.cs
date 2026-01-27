using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models.Facturas;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    public class AlbaranesVentaController : ApiController
    {
        private readonly IGestorAlbaranesVenta _gestor;
        public readonly IServicioAlbaranesVenta _servicio;
        private readonly IServicioFacturas _servicioFacturas;
        private readonly IGestorFacturas _gestorFacturas;

        public AlbaranesVentaController(IGestorAlbaranesVenta gestor, IServicioAlbaranesVenta servicio)
        {
            _gestor = gestor;
            _servicio = servicio;
            _servicioFacturas = new ServicioFacturas();
            _gestorFacturas = new GestorFacturas(_servicioFacturas);
        }

        // GET api/AlbaranesVenta
        [HttpGet]
        public async Task<HttpResponseMessage> GetAlbaran(string empresa, int numeroAlbaran, bool papelConMembrete = false)
        {
            FacturaLookup albaran = new FacturaLookup { Empresa = empresa, Factura = numeroAlbaran.ToString() };
            List<FacturaLookup> lista = new List<FacturaLookup>
            {
                albaran
            };
            List<Factura> albaranes = _gestorFacturas.LeerAlbaranes(lista);

            var result = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = _gestorFacturas.FacturasEnPDF(albaranes, papelConMembrete, User.Identity.Name)
            };
            result.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/pdf");

            return result;
        }

        [HttpPost]
        [Route("api/AlbaranesVenta/CrearAlbaran")]
        public async Task<IHttpActionResult> CrearAlbaran([FromBody] dynamic parametros)
        {
            string empresa = parametros.Empresa;
            int pedido = parametros.Pedido;
            string usuario = parametros.Usuario;
            if (empresa == null)
            {
                return BadRequest("No se ha especificado la empresa");
            }
            if (pedido == 0)
            {
                return BadRequest("No se ha especificado el pedido");
            }
            try
            {
                int albaran = await _gestor.CrearAlbaran(empresa, pedido, usuario);
                return Ok(albaran);
            }
            catch (System.Exception ex)
            {
                return InternalServerError(ex);
            }
        }
    }
}
