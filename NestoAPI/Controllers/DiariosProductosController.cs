using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Models;
using NestoAPI.Models.Productos;
using Newtonsoft.Json;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#554: con [Authorize] (el único llamante es Nesto, que manda el JWT) y el traspaso de
    /// diario con SQL parametrizado. Antes el POST era anónimo y concatenaba lo que llegaba en el cuerpo.
    /// </summary>
    [Authorize]
    public class DiariosProductosController : ApiController
    {
        private readonly NVEntities db;
        private readonly Func<string, object[], Task<int>> ejecutarSql;

        public DiariosProductosController() : this(new NVEntities())
        {
        }

        /// <param name="ejecutarSql">(sql, parámetros) → filas afectadas. Inyectable para tests.</param>
        internal DiariosProductosController(NVEntities db, Func<string, object[], Task<int>> ejecutarSql = null)
        {
            this.db = db;
            this.ejecutarSql = ejecutarSql ?? ((sql, parametros) => this.db.Database.ExecuteSqlCommandAsync(sql, parametros));
        }


        // GET: api/DiariosProductos
        public IQueryable<DiarioProductoDTO> GetDiariosProductos()
        {
            var diarios = db.DiariosProductos.Include(d => d.ExtractoProductoes).Where(d => d.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && !d.Sistema && !d.Número.StartsWith("_"));
            var almacenes = diarios.Select(d => new DiarioProductoDTO
            {
                Id = d.Número.Trim(),
                Descripcion = d.Descripción.Trim(),
                EstaVacio = !d.PreExtrProductoes.Any(),
                Almacenes = (new List<string> { "(todos)" }).Concat(d.PreExtrProductoes.Select(e => e.Almacén).Distinct()).ToList()
            });
            return almacenes;
        }
        /*
        // GET: api/DiariosProductos/5
        [ResponseType(typeof(DiarioProducto))]
        public async Task<IHttpActionResult> GetDiarioProducto(string id)
        {
            DiarioProducto diarioProducto = await db.DiariosProductos.FindAsync(id);
            if (diarioProducto == null)
            {
                return NotFound();
            }

            return Ok(diarioProducto);
        }

        // PUT: api/DiariosProductos/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutDiarioProducto(string id, DiarioProducto diarioProducto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != diarioProducto.Empresa)
            {
                return BadRequest();
            }

            db.Entry(diarioProducto).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!DiarioProductoExists(id))
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
        */

        // POST: api/DiariosProductos
        // Traspasa los movimientos pendientes (PreExtrProducto) de un diario a otro, opcionalmente solo los de un almacén.
        [HttpPost]
        [ResponseType(typeof(bool))]
        public async Task<IHttpActionResult> PostDiarioProducto(ParametrosDiarioProducto parametros)
        {
            if (parametros == null)
            {
                return BadRequest();
            }
            string diarioOrigen = parametros.diarioOrigen?.Trim();
            string diarioDestino = parametros.diarioDestino?.Trim();
            string almacen = parametros.almacen?.Trim();

            // Solo los diarios que ofrece el GET (de la empresa, no de sistema, no «_...»)
            List<string> diariosPermitidos = (await db.DiariosProductos
                .Where(d => d.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && !d.Sistema && !d.Número.StartsWith("_"))
                .Select(d => d.Número)
                .ToListAsync().ConfigureAwait(false))
                .Select(d => d?.Trim())
                .ToList();
            if (string.IsNullOrEmpty(diarioOrigen) || !diariosPermitidos.Contains(diarioOrigen))
            {
                return BadRequest($"El diario de origen '{diarioOrigen}' no existe o no se puede traspasar.");
            }
            if (string.IsNullOrEmpty(diarioDestino) || !diariosPermitidos.Contains(diarioDestino))
            {
                return BadRequest($"El diario de destino '{diarioDestino}' no existe o no se puede traspasar.");
            }

            string sql = "UPDATE PreExtrProducto SET Diario = @p0 WHERE Diario = @p1";
            List<object> valores = new List<object> { diarioDestino, diarioOrigen };
            if (!string.IsNullOrEmpty(almacen) && almacen != "(todos)")
            {
                sql += " AND Almacén = @p2";
                valores.Add(almacen);
            }

            int filasAfectadas = await ejecutarSql(sql, valores.ToArray()).ConfigureAwait(false);
            return filasAfectadas > 0
                ? (IHttpActionResult)Ok(true)
                : BadRequest("No se encontraron registros para actualizar");
        }


        /*
        // DELETE: api/DiariosProductos/5
        [ResponseType(typeof(DiarioProducto))]
        public async Task<IHttpActionResult> DeleteDiarioProducto(string id)
        {
            DiarioProducto diarioProducto = await db.DiariosProductos.FindAsync(id);
            if (diarioProducto == null)
            {
                return NotFound();
            }

            db.DiariosProductos.Remove(diarioProducto);
            await db.SaveChangesAsync();

            return Ok(diarioProducto);
        }
        */
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool DiarioProductoExists(string empresa, string id)
        {
            return db.DiariosProductos.Any(e => e.Empresa == empresa && e.Número == id);
        }
    }
}