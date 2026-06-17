using System.Data.Entity;
using System.Linq;
using System.Web.Http;
using NestoAPI.Infraestructure.Agencias.Tarifas;
using NestoAPI.Models;

namespace NestoAPI.Controllers
{
    /// <summary>Agencia + su recargo de combustible (fracción, p.ej. 0,1055 = 10,55 %).</summary>
    public class RecargoCombustibleAgenciaDTO
    {
        public int Numero { get; set; }
        public string Nombre { get; set; }
        public decimal RecargoCombustible { get; set; }
    }

    /// <summary>
    /// Tarifas de agencia server-side (Nesto#340): mantenimiento del recargo de combustible por
    /// agencia (editable mensual desde Nesto) y comparador "agencia más económica" para un pedido.
    /// </summary>
    public class AgenciasTarifasController : ApiController
    {
        public AgenciasTarifasController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
        }

        public AgenciasTarifasController(NVEntities db)
        {
            this.db = db;
            db.Configuration.LazyLoadingEnabled = false;
        }

        private readonly NVEntities db;

        // GET: api/Agencias/RecargosCombustible  -> lista para la UI de edición mensual.
        [HttpGet]
        [Route("api/Agencias/RecargosCombustible")]
        public IHttpActionResult GetRecargosCombustible()
        {
            var agencias = db.AgenciasTransportes
                .GroupBy(a => new { a.Numero, a.Nombre })
                .Select(g => new RecargoCombustibleAgenciaDTO
                {
                    Numero = g.Key.Numero,
                    Nombre = g.Key.Nombre,
                    RecargoCombustible = g.Max(a => a.RecargoCombustible)
                })
                .OrderBy(a => a.Numero)
                .ToList();

            return Ok(agencias);
        }

        // PUT: api/Agencias/{numero}/RecargoCombustible  -> actualiza el % de TODAS las empresas
        // de esa agencia (el fuel es a nivel de transportista).
        [HttpPut]
        [Route("api/Agencias/{numero:int}/RecargoCombustible")]
        public IHttpActionResult PutRecargoCombustible(int numero, [FromBody] RecargoCombustibleAgenciaDTO dto)
        {
            if (dto == null || dto.RecargoCombustible < 0)
            {
                return BadRequest("El recargo de combustible no puede ser nulo ni negativo.");
            }

            var filas = db.AgenciasTransportes.Where(a => a.Numero == numero).ToList();
            if (!filas.Any())
            {
                return NotFound();
            }

            foreach (var fila in filas)
            {
                fila.RecargoCombustible = dto.RecargoCombustible;
            }
            db.SaveChanges();

            return Ok(dto.RecargoCombustible);
        }

        // GET: api/Agencias/MasEconomica?empresa=&codigoPostal=&peso=&reembolso=
        [HttpGet]
        [Route("api/Agencias/MasEconomica")]
        public IHttpActionResult GetMasEconomica(string codigoPostal, decimal peso, string empresa = "1", decimal reembolso = 0)
        {
            var comparador = new ComparadorAgencias(new RegistroTarifas(), new ProveedorRecargoCombustibleEF(db));
            OpcionEnvioAgencia mejor = comparador.MasEconomica(empresa, codigoPostal, peso, reembolso);

            if (mejor == null)
            {
                return NotFound();
            }
            return Ok(mejor);
        }
    }
}
