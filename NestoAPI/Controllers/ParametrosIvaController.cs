using NestoAPI.Models;
using NestoAPI.Models.PedidosBase;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class ParametrosIvaController : ApiController
    {
        public ParametrosIvaController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }
        private readonly NVEntities db = new NVEntities();

        // GET: api/ParametrosIva
        [ResponseType(typeof(List<ParametrosIvaBase>))]
        public async Task<IHttpActionResult> GetParametrosIva(string empresa, string ivaCabecera)
        {
            var parametros = db.ParametrosIVA
                .Where(p => p.Empresa == empresa && p.IVA_Cliente_Prov == ivaCabecera)
                .Select(p => new ParametrosIvaBase
                {
                    CodigoIvaProducto = p.IVA_Producto.Trim(),
                    PorcentajeIvaProducto = (decimal)p.C__IVA / 100
                });

            return Ok(await parametros.ToListAsync());
        }
    }
}
