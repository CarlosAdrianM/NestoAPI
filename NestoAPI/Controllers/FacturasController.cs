using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class FacturasController : ApiController
    {
        private readonly IServicioFacturas servicio;
        private readonly IGestorFacturas gestor;

        public FacturasController()
        {
            servicio = new ServicioFacturas();
            gestor = new GestorFacturas(servicio);
        }
        
        // GET api/Facturas
        [HttpGet]
        [ResponseType(typeof(Factura))]
        public async Task<IHttpActionResult> GetFactura(string empresa, string numeroFactura)
        {
            Factura factura = gestor.LeerFactura(empresa, numeroFactura);

            return Ok(factura);
        }
    }
}