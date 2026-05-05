using NestoAPI.Models;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class PlanesVentajasController : ApiController
    {
        private readonly NVEntities db;

        public PlanesVentajasController()
        {
            db = new NVEntities();
        }

        public PlanesVentajasController(NVEntities db)
        {
            this.db = db;
        }

        // GET: api/PlanesVentajas/Estados
        [HttpGet]
        [Route("api/PlanesVentajas/Estados")]
        [ResponseType(typeof(List<EstadoPlanVentajas>))]
        public async Task<IHttpActionResult> GetEstados()
        {
            List<EstadoPlanVentajas> estados = await db.EstadosPlanesVentajas
                .OrderBy(e => e.Numero)
                .ToListAsync();
            return Ok(estados);
        }
    }
}
