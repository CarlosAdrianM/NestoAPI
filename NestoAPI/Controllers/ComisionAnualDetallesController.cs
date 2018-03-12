﻿using System;
using System.Linq;
using System.Web.Http;
using NestoAPI.Models;
using NestoAPI.Models.Comisiones;

namespace NestoAPI.Controllers
{
    public class ComisionAnualDetallesController : ApiController
    {
        private NVEntities db = new NVEntities();


        ServicioComisionesAnualesEstetica servicio = new ServicioComisionesAnualesEstetica();

        // GET: api/ComisionAnualDetalles
        public IQueryable<vstLinPedidoVtaComisionesDetalle> GetComisionesAnualesDetalles(string vendedor, int anno, int mes,bool incluirAlbaranes, string etiqueta)
        {
            int annoActual = DateTime.Today.Year;
            int mesActual = DateTime.Today.Month;

            if (annoActual == anno && mesActual == mes)
            {
                ServicioComisionesAnualesEstetica servicio = new ServicioComisionesAnualesEstetica();
                var etiquetaServicio = servicio.Etiquetas.Single(s => s.Nombre == etiqueta);
                var comisiones = etiquetaServicio.LeerVentaMesDetalle(vendedor, anno, mes, incluirAlbaranes, etiqueta).ToList();
                return comisiones.Select(c=>
                    new vstLinPedidoVtaComisionesDetalle
                    {
                        Vendedor = vendedor,
                        Anno = (short)anno,
                        Mes = (byte)mes,
                        Nombre = c.Nombre,
                        Direccion = c.Dirección,
                        BaseImponible = c.Base_Imponible,
                        Fecha_Factura = c.Fecha_Factura,
                        Empresa = c.Empresa,
                        Pedido = c.Número
                    }
                    ).AsQueryable();
            }

            var detalle = db.vstLinPedidoVtaComisionesDetalles.Where(v => v.Vendedor == vendedor && v.Anno == anno && v.Mes == mes && v.Etiqueta == etiqueta);
            
            return detalle;
        }
        
        /*
        // GET: api/ComisionAnualDetalles/5
        [ResponseType(typeof(ComisionAnualDetalle))]
        public async Task<IHttpActionResult> GetComisionAnualDetalle()
        {
            ComisionAnualDetalle comisionAnualDetalle = await db.ComisionesAnualesDetalles.FindAsync(id);
            if (comisionAnualDetalle == null)
            {
                return NotFound();
            }

            return Ok(comisionAnualDetalle);
        }
        */
        /*
        // PUT: api/ComisionAnualDetalles/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutComisionAnualDetalle(int id, ComisionAnualDetalle comisionAnualDetalle)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != comisionAnualDetalle.Id)
            {
                return BadRequest();
            }

            db.Entry(comisionAnualDetalle).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ComisionAnualDetalleExists(id))
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

        // POST: api/ComisionAnualDetalles
        [ResponseType(typeof(ComisionAnualDetalle))]
        public async Task<IHttpActionResult> PostComisionAnualDetalle(ComisionAnualDetalle comisionAnualDetalle)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.ComisionesAnualesDetalles.Add(comisionAnualDetalle);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (ComisionAnualDetalleExists(comisionAnualDetalle.Id))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = comisionAnualDetalle.Id }, comisionAnualDetalle);
        }

        // DELETE: api/ComisionAnualDetalles/5
        [ResponseType(typeof(ComisionAnualDetalle))]
        public async Task<IHttpActionResult> DeleteComisionAnualDetalle(int id)
        {
            ComisionAnualDetalle comisionAnualDetalle = await db.ComisionesAnualesDetalles.FindAsync(id);
            if (comisionAnualDetalle == null)
            {
                return NotFound();
            }

            db.ComisionesAnualesDetalles.Remove(comisionAnualDetalle);
            await db.SaveChangesAsync();

            return Ok(comisionAnualDetalle);
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

        private bool ComisionAnualDetalleExists(int id)
        {
            return db.ComisionesAnualesDetalles.Count(e => e.Id == id) > 0;
        }
    }
}