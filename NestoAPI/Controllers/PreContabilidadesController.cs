using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;

namespace NestoAPI.Controllers
{
    public class PreContabilidadesController : ApiController
    {
        private NVEntities db = new NVEntities();
        private readonly IContabilidadService servicio = new ContabilidadService();
        private readonly GestorContabilidad gestorContabilidad;
        public PreContabilidadesController()
        {
            gestorContabilidad = new GestorContabilidad(servicio);
        }

        // GET: api/PreContabilidades
        public IQueryable<PreContabilidad> GetPreContabilidades()
        {
            return db.PreContabilidades;
        }

        // GET: api/PreContabilidades/5
        [ResponseType(typeof(PreContabilidad))]
        public async Task<IHttpActionResult> GetPreContabilidad(string id)
        {
            PreContabilidad preContabilidad = await db.PreContabilidades.FindAsync(id);
            if (preContabilidad == null)
            {
                return NotFound();
            }

            return Ok(preContabilidad);
        }

        // PUT: api/PreContabilidades/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutPreContabilidad(int id, PreContabilidad preContabilidad)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != preContabilidad.Nº_Orden)
            {
                return BadRequest();
            }

            db.Entry(preContabilidad).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!PreContabilidadExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return StatusCode(HttpStatusCode.NoContent);
        }

        // POST: api/PreContabilidades
        [ResponseType(typeof(int))]
        public async Task<IHttpActionResult> PostPreContabilidad([FromBody] dynamic parametros)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            List<PreContabilidad> lineas = parametros.lineas.ToObject<List<PreContabilidad>>();
            bool contabilizar = parametros.contabilizar;

            int asiento;
            if (contabilizar)
            {
                asiento = await gestorContabilidad.CrearLineasDiarioYContabilizar(lineas);
            }
            else
            {
                asiento = await gestorContabilidad.CrearLineasDiario(lineas);
            }

            return Ok(asiento);
        }

        // DELETE: api/PreContabilidades/5
        [ResponseType(typeof(PreContabilidad))]
        public async Task<IHttpActionResult> DeletePreContabilidad(string id)
        {
            PreContabilidad preContabilidad = await db.PreContabilidades.FindAsync(id);
            if (preContabilidad == null)
            {
                return NotFound();
            }

            db.PreContabilidades.Remove(preContabilidad);
            await db.SaveChangesAsync();

            return Ok(preContabilidad);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool PreContabilidadExists(int id)
        {
            return db.PreContabilidades.Count(e => e.Nº_Orden == id) > 0;
        }
    }
}