using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#553: reposición de tiendas desde Nesto (sacarla de Nesto viejo), por fases. Fase 1, parte de
    /// lectura: la propuesta que calcula prdRellenarReposicionStock, sin escribir nada.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Reposiciones")]
    public class ReposicionesController : ApiController
    {
        private readonly NVEntities db;

        public ReposicionesController() : this(new NVEntities())
        {
        }

        internal ReposicionesController(NVEntities db)
        {
            this.db = db;
        }

        // GET api/Reposiciones/Propuesta?origen=ALG&destino=REI&empresa=1
        /// <summary>
        /// Qué habría que mandar de <paramref name="origen"/> a <paramref name="destino"/> (stock máximo, pendientes
        /// y stock de los dos almacenes). 400 si los almacenes no valen o si en el destino hay una reposición
        /// anterior sin contabilizar (el procedimiento no deja mezclarlas).
        /// </summary>
        [HttpGet]
        [Route("Propuesta")]
        [ResponseType(typeof(List<LineaPropuestaReposicionDTO>))]
        public async Task<IHttpActionResult> GetPropuesta(string origen, string destino, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            List<LineaPropuestaReposicionDTO> propuesta = await new ServicioPropuestaReposicion(db)
                .CalcularPropuesta(empresa, origen, destino).ConfigureAwait(false);
            return Ok(propuesta);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
