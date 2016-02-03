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

namespace NestoAPI.Controllers
{
    public class ExtractosClienteController : ApiController
    {
        private NVEntities db = new NVEntities();

        // Carlos 25/01/16: lo pongo para desactivar el Lazy Loading
        public ExtractosClienteController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }


        // GET: api/ExtractosCliente
        public IQueryable<ExtractoClienteDTO> GetExtractosCliente(string cliente)
        {
            List<ExtractoClienteDTO> extracto = db.ExtractosCliente.Where(e => e.Número == cliente && e.ImportePdte != 0)
                .Select(extractoEncontrado => new ExtractoClienteDTO
                {
                    id = extractoEncontrado.Nº_Orden,
                    empresa = extractoEncontrado.Empresa,
                    asiento = extractoEncontrado.Asiento,
                    cliente = extractoEncontrado.Número,
                    contacto = extractoEncontrado.Contacto,
                    fecha = extractoEncontrado.Fecha,
                    tipo = extractoEncontrado.TipoApunte,
                    documento = extractoEncontrado.Nº_Documento,
                    efecto = extractoEncontrado.Efecto,
                    concepto = extractoEncontrado.Concepto,
                    importe = extractoEncontrado.Importe,
                    importePendiente = extractoEncontrado.ImportePdte,
                    vendedor = extractoEncontrado.Vendedor,
                    vencimiento = extractoEncontrado.FechaVto,
                    ccc = extractoEncontrado.CCC,
                    ruta = extractoEncontrado.Ruta,
                    estado = extractoEncontrado.Estado
                }).ToList();
            return extracto.AsQueryable();
        }

        // GET: api/ExtractosCliente
        [ResponseType(typeof(Mod347DTO))]
        public async Task<IHttpActionResult> GetModelo347(string empresa, string cliente, string NIF)
        {
            Mod347DTO modelo = new Mod347DTO();

            Cliente clienteComprobacion = await db.Clientes.SingleOrDefaultAsync(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.ClientePrincipal == true);
            if (clienteComprobacion.CIF_NIF != NIF)
            {
                throw new Exception("El NIF no es correcto");
            }

            modelo.nombre = clienteComprobacion.Nombre != null ? clienteComprobacion.Nombre.Trim() : "";
            modelo.direccion = clienteComprobacion.Dirección != null ? clienteComprobacion.Dirección.Trim() : "";
            modelo.codigoPostal = clienteComprobacion.CodPostal != null ? clienteComprobacion.CodPostal.Trim() : "";

            DateTime hoy = System.DateTime.Today;
            DateTime fechaDesde = new DateTime(hoy.AddYears(-1).Year, 1, 1);
            DateTime fechaHasta = new DateTime(hoy.Year, 1, 1);
            

            List<ExtractoClienteDTO> extracto = await db.ExtractosCliente.Where(e => e.Empresa == empresa && e.Número == cliente && e.TipoApunte == "1" && e.Fecha >= fechaDesde && e.Fecha < fechaHasta)
                .Select(extractoEncontrado => new ExtractoClienteDTO
                {
                    id = extractoEncontrado.Nº_Orden,
                    empresa = extractoEncontrado.Empresa,
                    asiento = extractoEncontrado.Asiento,
                    cliente = extractoEncontrado.Número,
                    contacto = extractoEncontrado.Contacto,
                    fecha = extractoEncontrado.Fecha,
                    tipo = extractoEncontrado.TipoApunte,
                    documento = extractoEncontrado.Nº_Documento,
                    efecto = extractoEncontrado.Efecto,
                    concepto = extractoEncontrado.Concepto,
                    importe = extractoEncontrado.Importe,
                    importePendiente = extractoEncontrado.ImportePdte,
                    vendedor = extractoEncontrado.Vendedor,
                    vencimiento = extractoEncontrado.FechaVto,
                    ccc = extractoEncontrado.CCC,
                    ruta = extractoEncontrado.Ruta,
                    estado = extractoEncontrado.Estado
                })
                .OrderBy(e => e.fecha)
                .ToListAsync();


            modelo.trimestre = new decimal[4];
            for (int i=0; i<=3; i++)
            {
                modelo.trimestre[i] = extracto.Where(e => (e.fecha.Month + 2) / 3 == i+1).Sum(e => e.importe);
            }
            
            
            modelo.MovimientosMayor = extracto;

            return Ok(modelo);
        }


        // GET: api/ExtractosCliente/5
        [ResponseType(typeof(ExtractoCliente))]
        public async Task<IHttpActionResult> GetExtractoCliente(string id)
        {
            ExtractoCliente extractoCliente = await db.ExtractosCliente.FindAsync(id);
            if (extractoCliente == null)
            {
                return NotFound();
            }

            return Ok(extractoCliente);
        }


        // PUT: api/ExtractosCliente/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutExtractoCliente(string id, ExtractoCliente extractoCliente)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != extractoCliente.Empresa)
            {
                return BadRequest();
            }

            db.Entry(extractoCliente).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ExtractoClienteExists(id))
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

        // POST: api/ExtractosCliente
        [ResponseType(typeof(ExtractoCliente))]
        public async Task<IHttpActionResult> PostExtractoCliente(ExtractoCliente extractoCliente)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.ExtractosCliente.Add(extractoCliente);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (ExtractoClienteExists(extractoCliente.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = extractoCliente.Empresa }, extractoCliente);
        }

        // DELETE: api/ExtractosCliente/5
        [ResponseType(typeof(ExtractoCliente))]
        public async Task<IHttpActionResult> DeleteExtractoCliente(string id)
        {
            ExtractoCliente extractoCliente = await db.ExtractosCliente.FindAsync(id);
            if (extractoCliente == null)
            {
                return NotFound();
            }

            db.ExtractosCliente.Remove(extractoCliente);
            await db.SaveChangesAsync();

            return Ok(extractoCliente);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool ExtractoClienteExists(string id)
        {
            return db.ExtractosCliente.Count(e => e.Empresa == id) > 0;
        }
    }
}