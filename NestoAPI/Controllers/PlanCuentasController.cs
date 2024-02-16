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
using NestoAPI.Models.Cajas;

namespace NestoAPI.Controllers
{
    public class PlanCuentasController : ApiController
    {
        private NVEntities db = new NVEntities();

        // GET: api/PlanCuentas
        public IQueryable<CuentaContableDTO> GetPlanCuentas(string empresa, string grupo = "")
        {
            var resultado = db.PlanCuentas.Where(p => p.Empresa == empresa && p.Estado >= Constantes.Cuentas.ESTADO_ACTIVA && p.Nº_Cuenta.Length == Constantes.Cuentas.NIVEL_MAXIMO);
            if (!string.IsNullOrEmpty(grupo))
            {
                resultado = resultado.Where(p => p.Nº_Cuenta.StartsWith(grupo));
            }
            return resultado.Select(p => new CuentaContableDTO {
                Cuenta = p.Nº_Cuenta.Trim(),
                Nombre = p.Concepto.Trim()
            });
        }

        // GET: api/PlanCuentas/5
        [ResponseType(typeof(PlanCuenta))]
        public async Task<IHttpActionResult> GetPlanCuenta(string id)
        {
            PlanCuenta planCuenta = await db.PlanCuentas.FindAsync(id);
            if (planCuenta == null)
            {
                return NotFound();
            }

            return Ok(planCuenta);
        }

        // PUT: api/PlanCuentas/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutPlanCuenta(string empresa, string id, PlanCuenta planCuenta)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != planCuenta.Empresa)
            {
                return BadRequest();
            }

            db.Entry(planCuenta).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!PlanCuentaExists(empresa, id))
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

        // POST: api/PlanCuentas
        [ResponseType(typeof(PlanCuenta))]
        public async Task<IHttpActionResult> PostPlanCuenta(PlanCuenta planCuenta)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.PlanCuentas.Add(planCuenta);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (PlanCuentaExists(planCuenta.Empresa, planCuenta.Nº_Cuenta))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = planCuenta.Empresa }, planCuenta);
        }

        // DELETE: api/PlanCuentas/5
        [ResponseType(typeof(PlanCuenta))]
        public async Task<IHttpActionResult> DeletePlanCuenta(string id)
        {
            PlanCuenta planCuenta = await db.PlanCuentas.FindAsync(id);
            if (planCuenta == null)
            {
                return NotFound();
            }

            db.PlanCuentas.Remove(planCuenta);
            await db.SaveChangesAsync();

            return Ok(planCuenta);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool PlanCuentaExists(string empresa, string id)
        {
            return db.PlanCuentas.Count(e => e.Empresa == empresa && e.Nº_Cuenta == id) > 0;
        }
    }
}