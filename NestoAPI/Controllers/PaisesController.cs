using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#355 / Nesto#428: catálogo de países (tabla Paises, diseño #148). No está en el
    /// EDMX, así que se lee por SQL crudo (patrón Cargos/EstadosCCC). Alimenta el SelectorPais
    /// del alta de cliente.
    /// </summary>
    public class PaisesController : ApiController
    {
        private readonly NVEntities db;

        public PaisesController()
        {
            db = new NVEntities();
        }

        internal PaisesController(NVEntities db)
        {
            this.db = db;
        }

        // GET: api/Paises
        [HttpGet]
        [Route("api/Paises")]
        [ResponseType(typeof(List<PaisDTO>))]
        public async Task<IHttpActionResult> GetPaises()
        {
            const string sql = @"SELECT LTRIM(RTRIM(Codigo)) AS Codigo, LTRIM(RTRIM(Nombre)) AS Nombre,
                                    UnionEuropea
                                 FROM Paises
                                 ORDER BY Nombre";
            List<PaisDTO> paises = await db.Database.SqlQuery<PaisDTO>(sql)
                .ToListAsync()
                .ConfigureAwait(false);
            return Ok(paises);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
