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
    public class DiariosProductosController : ApiController
    {
        private NVEntities db = new NVEntities();


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
        [HttpPost]
        [ResponseType(typeof(bool))]
        public async Task<IHttpActionResult> PostDiarioProducto(ParametrosDiarioProducto parametros)
        {
            try
            {
                if (parametros == null)
                {
                    return BadRequest();
                }
                // Obtener los valores de los parámetros individuales
                string diarioOrigen = parametros.diarioOrigen;
                string diarioDestino = parametros.diarioDestino;
                string almacen = parametros.almacen;

                // Construye tu consulta T-SQL
                string sqlQuery = $"UPDATE PreExtrProducto SET Diario = '{diarioDestino}' WHERE Diario = '{diarioOrigen}'";

                if (!string.IsNullOrEmpty(almacen) && almacen != "(todos)")
                {
                    sqlQuery += $" and Almacén = '{almacen}'";
                }

                // Ejecuta la consulta
                int filasAfectadas = db.Database.ExecuteSqlCommand(sqlQuery);

                // Verifica si hubo filas afectadas
                if (filasAfectadas > 0)
                {
                    return Ok(true);
                }
                else
                {
                    return BadRequest("No se encontraron registros para actualizar");
                }

            }
            catch (Exception ex)
            {
                throw ex;
            }


            
            return Ok(false); 
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