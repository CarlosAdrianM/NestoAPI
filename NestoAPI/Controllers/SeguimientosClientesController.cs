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
using System.Data.Entity.Core.Objects;

namespace NestoAPI.Controllers
{
    public class SeguimientosClientesController : ApiController
    {
        private NVEntities db = new NVEntities();

        // Carlos 13/03/17: lo pongo para desactivar el Lazy Loading
        public SeguimientosClientesController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }

        // GET: api/SeguimientosClientes
        public IQueryable<SeguimientoClienteDTO> GetSeguimientosClientes(string empresa, string cliente, string contacto)
        {
            DateTime fechaDesde = DateTime.Today.AddYears(-3);
            return db.SeguimientosClientes
                .Include(s => s.Cliente)
                .Where(s => s.Empresa == empresa && s.Número == cliente && s.Contacto == contacto && s.Fecha >= fechaDesde)
                .Select(s => new SeguimientoClienteDTO {
                    Aparatos = s.Aparatos,
                    Aviso = s.Aviso,
                    Cliente = s.Número,
                    ClienteNuevo = s.ClienteNuevo,
                    Comentarios = s.Comentarios,
                    Contacto = s.Contacto,
                    Direccion = s.Cliente.Dirección,
                    Empresa = s.Empresa,
                    Estado = (SeguimientoClienteDTO.EstadoSeguimientoDTO)s.Estado,
                    Fecha = s.Fecha,
                    GestionAparatos = s.GestiónAparatos,
                    Id = s.NºOrden,
                    Nombre = s.Cliente.Nombre,
                    Pedido = s.Pedido,
                    PrimeraVisita = s.PrimeraVisita,
                    Tipo = s.Tipo,
                    Usuario = s.Usuario,
                    Vendedor = s.Vendedor
                })
                .OrderByDescending(s=> s.Id);
        }

        // GET: api/SeguimientosClientes
        public IQueryable<SeguimientoClienteDTO> GetSeguimientosClientes(string vendedor, DateTime fecha)
        {
            DateTime fechaDesde = new DateTime(fecha.Year, fecha.Month, fecha.Day);
            DateTime fechaHasta = fechaDesde.AddDays(1);

            return db.SeguimientosClientes
                .Include(s => s.Cliente)
                .Where(s => s.Vendedor == vendedor && s.Fecha >= fechaDesde && s.Fecha < fechaHasta)
                .Select(s => new SeguimientoClienteDTO
                {
                    Aparatos = s.Aparatos,
                    Aviso = s.Aviso,
                    Cliente = s.Número,
                    ClienteNuevo = s.ClienteNuevo,
                    Comentarios = s.Comentarios,
                    Contacto = s.Contacto,
                    Direccion = s.Cliente.Dirección,
                    Empresa = s.Empresa,
                    Estado = (SeguimientoClienteDTO.EstadoSeguimientoDTO)s.Estado,
                    Fecha = s.Fecha,
                    GestionAparatos = s.GestiónAparatos,
                    Id = s.NºOrden,
                    Nombre = s.Cliente.Nombre,
                    Pedido = s.Pedido,
                    PrimeraVisita = s.PrimeraVisita,
                    Tipo = s.Tipo,
                    Usuario = s.Usuario,
                    Vendedor = s.Vendedor
                })
                .OrderBy(s => s.Id);
        }

        /*
        // GET: api/SeguimientosClientes/5
        [ResponseType(typeof(SeguimientoCliente))]
        public async Task<IHttpActionResult> GetSeguimientoCliente(string id)
        {
            SeguimientoCliente seguimientoCliente = await db.SeguimientosClientes.FindAsync(id);
            if (seguimientoCliente == null)
            {
                return NotFound();
            }

            return Ok(seguimientoCliente);
        }
        */
        /*
        // PUT: api/SeguimientosClientes/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutSeguimientoCliente(string id, SeguimientoCliente seguimientoCliente)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != seguimientoCliente.Empresa)
            {
                return BadRequest();
            }

            db.Entry(seguimientoCliente).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SeguimientoClienteExists(id))
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

        // POST: api/SeguimientosClientes
        [ResponseType(typeof(SeguimientoCliente))]
        public async Task<IHttpActionResult> PostSeguimientoCliente(SeguimientoClienteDTO seguimientoClienteDTO)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            SeguimientoCliente seguimientoCliente = new SeguimientoCliente
            {
                Aparatos = seguimientoClienteDTO.Aparatos,
                Aviso = seguimientoClienteDTO.Aviso,
                ClienteNuevo = seguimientoClienteDTO.ClienteNuevo,
                Comentarios = seguimientoClienteDTO.Comentarios,
                Contacto = seguimientoClienteDTO.Contacto,
                Empresa = seguimientoClienteDTO.Empresa,
                Estado = (short)seguimientoClienteDTO.Estado,
                Fecha = seguimientoClienteDTO.Fecha,
                GestiónAparatos = seguimientoClienteDTO.GestionAparatos,
                Número = seguimientoClienteDTO.Cliente,
                PrimeraVisita = seguimientoClienteDTO.PrimeraVisita,
                Tipo = seguimientoClienteDTO.Tipo,
                Usuario = seguimientoClienteDTO.Usuario,
                Vendedor = seguimientoClienteDTO.Vendedor
            };



            db.SeguimientosClientes.Add(seguimientoCliente);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                if (SeguimientoClienteExists(seguimientoCliente.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = seguimientoCliente.Empresa }, seguimientoCliente);
        }

        // DELETE: api/SeguimientosClientes/5
        [ResponseType(typeof(SeguimientoCliente))]
        public async Task<IHttpActionResult> DeleteSeguimientoCliente(string id)
        {
            SeguimientoCliente seguimientoCliente = await db.SeguimientosClientes.FindAsync(id);
            if (seguimientoCliente == null)
            {
                return NotFound();
            }

            db.SeguimientosClientes.Remove(seguimientoCliente);
            await db.SaveChangesAsync();

            return Ok(seguimientoCliente);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool SeguimientoClienteExists(string id)
        {
            return db.SeguimientosClientes.Count(e => e.Empresa == id) > 0;
        }
    }
}