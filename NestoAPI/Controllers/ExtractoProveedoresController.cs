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
using NestoAPI.Models.Bancos;

namespace NestoAPI.Controllers
{
    public class ExtractoProveedoresController : ApiController
    {
        private readonly NVEntities db;

        public ExtractoProveedoresController() : this(new NVEntities())
        {
        }

        // Nesto#340: inyección para tests (patrón del resto de controllers).
        internal ExtractoProveedoresController(NVEntities db)
        {
            this.db = db;
            db.Configuration.LazyLoadingEnabled = false;
        }

        /// <summary>
        /// Nesto#340: asiento del apunte de PAGO (TipoApunte 3) de un proveedor por fecha e
        /// importe exactos. Sustituye la consulta EF directa del módulo CanalesExternos de
        /// Nesto (cuadre de pagos de Amazon). Sin coincidencia devuelve 0, el mismo contrato
        /// que aplicaba el cliente.
        /// </summary>
        [HttpGet]
        [Route("api/ExtractoProveedores/BuscarPago")]
        [ResponseType(typeof(int))]
        public async Task<IHttpActionResult> BuscarPago(string proveedor, DateTime fecha, decimal importe)
        {
            const string TIPO_APUNTE_PAGO = "3";
            int asiento = await db.ExtractosProveedor
                .Where(e => e.Número == proveedor && e.Fecha == fecha && e.Importe == importe
                    && e.TipoApunte == TIPO_APUNTE_PAGO)
                .Select(e => e.Asiento)
                .FirstOrDefaultAsync();
            return Ok(asiento);
        }

        // GET: api/ExtractoProveedores
        public IQueryable<ExtractoProveedor> GetExtractosProveedor()
        {
            return db.ExtractosProveedor;
        }

        // GET: api/ExtractoProveedores/5
        [ResponseType(typeof(ExtractoProveedor))]
        public async Task<IHttpActionResult> GetExtractoProveedor(string id)
        {
            ExtractoProveedor extractoProveedor = await db.ExtractosProveedor.FindAsync(id);
            if (extractoProveedor == null)
            {
                return NotFound();
            }

            return Ok(extractoProveedor);
        }

        // GET: api/ExtractoProveedores/5
        [ResponseType(typeof(List<ExtractoProveedorDTO>))]
        public async Task<IHttpActionResult> GetExtractoProveedor(string empresa, int asiento)
        {
            List<ExtractoProveedorDTO> extracto = await db.ExtractosProveedor
                .Where(e => e.Empresa == empresa && e.Asiento == asiento)
                .Select(e => new ExtractoProveedorDTO {
                    Empresa = e.Empresa.Trim(),
                    Proveedor = e.Número.Trim(),
                    Nombre = e.Proveedore != null ? e.Proveedore.Nombre.Trim() : e.Número.Trim(),
                    Contacto = e.Contacto.Trim(),
                    Documento = e.NºDocumento.Trim(),
                    DocumentoProveedor = e.NºDocumentoProv.Trim(),
                    Delegacion = e.Delegación,
                    FormaVenta = e.FormaVenta,
                    Importe = e.Importe,
                    ImportePendiente = e.ImportePdte
                })
                .ToListAsync();
            if (extracto == null || !extracto.Any())
            {
                return Ok(new List<ExtractoProveedorDTO>());
            }

            return Ok(extracto);
        }

        // PUT: api/ExtractoProveedores/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutExtractoProveedor(string id, ExtractoProveedor extractoProveedor)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != extractoProveedor.Empresa)
            {
                return BadRequest();
            }

            db.Entry(extractoProveedor).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ExtractoProveedorExists(id))
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

        // POST: api/ExtractoProveedores
        [ResponseType(typeof(ExtractoProveedor))]
        public async Task<IHttpActionResult> PostExtractoProveedor(ExtractoProveedor extractoProveedor)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.ExtractosProveedor.Add(extractoProveedor);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (ExtractoProveedorExists(extractoProveedor.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = extractoProveedor.Empresa }, extractoProveedor);
        }

        // DELETE: api/ExtractoProveedores/5
        [ResponseType(typeof(ExtractoProveedor))]
        public async Task<IHttpActionResult> DeleteExtractoProveedor(string id)
        {
            ExtractoProveedor extractoProveedor = await db.ExtractosProveedor.FindAsync(id);
            if (extractoProveedor == null)
            {
                return NotFound();
            }

            db.ExtractosProveedor.Remove(extractoProveedor);
            await db.SaveChangesAsync();

            return Ok(extractoProveedor);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool ExtractoProveedorExists(string id)
        {
            return db.ExtractosProveedor.Count(e => e.Empresa == id) > 0;
        }
    }
}