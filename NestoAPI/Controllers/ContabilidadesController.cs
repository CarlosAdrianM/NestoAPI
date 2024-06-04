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
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;
using NestoAPI.Models.ApuntesBanco;
using NestoAPI.Models.Cajas;

namespace NestoAPI.Controllers
{
    public class ContabilidadesController : ApiController
    {
        private NVEntities db = new NVEntities();

        public ContabilidadesController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }

        // GET: api/Contabilidades
        public IQueryable<Contabilidad> GetContabilidades()
        {
            return db.Contabilidades;
        }

        // GET: api/Contabilidades/5
        [ResponseType(typeof(Contabilidad))]
        public async Task<IHttpActionResult> GetContabilidad(string id)
        {
            Contabilidad contabilidad = await db.Contabilidades.FindAsync(id);
            if (contabilidad == null)
            {
                return NotFound();
            }

            return Ok(contabilidad);
        }

        // GET: api/Contabilidades/5
        [ResponseType(typeof(List<ContabilidadDTO>))]
        public async Task<IHttpActionResult> GetContabilidad(string cuenta, bool punteado)
        {
            try
            {
                // Realizar la consulta y proyección
                List<ContabilidadDTO> apuntesDTO = await db.Contabilidades
                    .Where(c => c.Nº_Cuenta == cuenta && c.Punteado == punteado)
                    .Select(c => new ContabilidadDTO
                    {
                        Id = c.Nº_Orden,
                        Empresa = c.Empresa,
                        Cuenta = c.Nº_Cuenta,
                        Concepto = c.Concepto,
                        Debe = c.Debe,
                        Haber = c.Haber,
                        Fecha = c.Fecha,
                        Documento = c.Nº_Documento,
                        Asiento = (int)c.Asiento,
                        Diario = c.Diario,
                        Delegacion = c.Delegación,
                        FormaVenta = c.FormaVenta,
                        Departamento = c.Departamento,
                        CentroCoste = c.CentroCoste,
                        Usuario = c.Usuario.Trim()
                    })
                    .ToListAsync();

                if (apuntesDTO == null)
                {
                    return NotFound();
                }

                return Ok(apuntesDTO);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        // GET: api/Contabilidades/5
        [ResponseType(typeof(List<ContabilidadDTO>))]
        public async Task<IHttpActionResult> GetContabilidad(string empresa, int asiento)
        {
            try
            {
                // Realizar la consulta y proyección
                List<ContabilidadDTO> apuntesDTO = await db.Contabilidades
                    .Where(c => c.Empresa == empresa && c.Asiento == asiento)
                    .Select(c => new ContabilidadDTO
                    {
                        Id = c.Nº_Orden,
                        Empresa = c.Empresa,
                        Cuenta = c.Nº_Cuenta,
                        Concepto = c.Concepto,
                        Debe = c.Debe,
                        Haber = c.Haber,
                        Fecha = c.Fecha,
                        Documento = c.Nº_Documento,
                        Asiento = (int)c.Asiento,
                        Diario = c.Diario,
                        Delegacion = c.Delegación,
                        FormaVenta = c.FormaVenta,
                        Departamento = c.Departamento,
                        CentroCoste = c.CentroCoste,
                        Usuario = c.Usuario.Trim()
                    })
                    .ToListAsync();

                if (apuntesDTO == null)
                {
                    return NotFound();
                }
                /*
                foreach (var apunte in apuntesDTO)
                {
                    apunte.EstadoPunteo = ObtenerEstadoPunteo(apunte.Id, apunte.Debe - apunte.Haber);
                }
                */
                return Ok(apuntesDTO);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        // GET: api/Contabilidades/5 -> Saldo a una fecha
        [ResponseType(typeof(decimal))]
        public async Task<IHttpActionResult> GetContabilidad(string empresa, string cuenta, DateTime fecha)
        {
            var fechaDiaSiguiente = new DateTime(fecha.Year, fecha.Month, fecha.Day).AddDays(1);
            

            try
            {
                IQueryable<Contabilidad> contabilidadEmpresa;

                if (string.IsNullOrEmpty(empresa))
                {
                    contabilidadEmpresa = db.Contabilidades;
                }
                else
                {
                    contabilidadEmpresa = db.Contabilidades
                    .Where(c => c.Empresa == empresa);
                }

                var fechaUltimoCierre = await contabilidadEmpresa
                .Where(c => c.Diario == Constantes.Contabilidad.Diarios.DIARIO_CIERRE)
                .Select(c => DbFunctions.TruncateTime(c.Fecha)) // para que no coja la hora, minutos, ni segundos
                .MaxAsync();

                var saldo = await contabilidadEmpresa
                    .Where(c => c.Nº_Cuenta == cuenta && c.Fecha >= fechaUltimoCierre && c.Fecha < fechaDiaSiguiente)
                    .Select(c => c.Debe - c.Haber)
                    .DefaultIfEmpty()
                    .SumAsync();

                return Ok(saldo);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        // GET: api/Contabilidades/5
        [ResponseType(typeof(List<ContabilidadDTO>))]
        public async Task<IHttpActionResult> GetContabilidad(string empresa, string cuenta, DateTime fechaDesde, DateTime fechaHasta)
        {
            var fechaHastaMenor = fechaHasta.AddDays(1);
            try
            {
                IQueryable<Contabilidad> contabilidadEmpresa;

                if (string.IsNullOrEmpty(empresa))
                {
                    contabilidadEmpresa = db.Contabilidades;
                }
                else
                {
                    contabilidadEmpresa = db.Contabilidades
                    .Where(c => c.Empresa == empresa);
                }

                // Realizar la consulta y proyección
                List<ContabilidadDTO> apuntesDTO = await contabilidadEmpresa
                    .Where(c => c.Nº_Cuenta == cuenta && c.Fecha >= fechaDesde && c.Fecha < fechaHastaMenor)
                    .Select(c => new ContabilidadDTO
                    {
                        Id = c.Nº_Orden,
                        Empresa = c.Empresa.Trim(),
                        Cuenta = c.Nº_Cuenta.Trim(),
                        Concepto = c.Concepto.Trim(),
                        Debe = c.Debe,
                        Haber = c.Haber,
                        Fecha = c.Fecha,
                        Documento = c.Nº_Documento,
                        Asiento = (int)c.Asiento,
                        Diario = c.Diario,
                        Delegacion = c.Delegación,
                        FormaVenta = c.FormaVenta,
                        Departamento = c.Departamento,
                        CentroCoste = c.CentroCoste,
                        Usuario = c.Usuario.Trim()
                    })
                    .ToListAsync();

                if (apuntesDTO == null)
                {
                    return NotFound();
                }

                foreach (var apunte in apuntesDTO)
                {
                    apunte.EstadoPunteo = ObtenerEstadoPunteo(apunte.Id, apunte.Debe - apunte.Haber);
                }

                return Ok(apuntesDTO);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        [HttpGet]
        [Route("api/Contabilidades/LeerCuentasPorConcepto")]
        [ResponseType(typeof(List<ContabilidadDTO>))]
        public async Task<IHttpActionResult> LeerCuentasPorConcepto(string empresa, string concepto, DateTime fechaDesde, DateTime fechaHasta)
        {
            try
            {
                var consultaInicial = db.Contabilidades
                .Where(c => c.Empresa == empresa && c.Concepto.Contains(concepto) &&
                        c.Fecha >= fechaDesde && c.Fecha <= fechaHasta && c.Nº_Cuenta.StartsWith("6") &&
                        !db.ExtractosProveedor.Any(e => e.Empresa == c.Empresa && e.Asiento == c.Asiento)
                );

                var resultado = await consultaInicial
                    .GroupBy(c => new
                    {
                        Cuenta = c.Nº_Cuenta.Trim(),
                        Delegacion = c.Delegación,
                        Departamento = c.Departamento,
                        CentroCoste = c.CentroCoste
                    })
                    .Select(group => new ContabilidadDTO
                    {
                        Cuenta = group.Key.Cuenta,
                        Delegacion = group.Key.Delegacion,
                        Departamento = group.Key.Departamento,
                        CentroCoste = group.Key.CentroCoste,
                        FormaVenta = consultaInicial
                            .OrderByDescending(g => g.Fecha)
                            .FirstOrDefault().FormaVenta
                    })
                    .ToListAsync();

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
            
        }

        // PUT: api/Contabilidades/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutContabilidad(string id, Contabilidad contabilidad)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != contabilidad.Empresa)
            {
                return BadRequest();
            }

            db.Entry(contabilidad).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ContabilidadExists(id))
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

        // POST: api/Contabilidades
        [ResponseType(typeof(Contabilidad))]
        public async Task<IHttpActionResult> PostContabilidad(Contabilidad contabilidad)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.Contabilidades.Add(contabilidad);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (ContabilidadExists(contabilidad.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = contabilidad.Empresa }, contabilidad);
        }

        // DELETE: api/Contabilidades/5
        [ResponseType(typeof(Contabilidad))]
        public async Task<IHttpActionResult> DeleteContabilidad(string id)
        {
            Contabilidad contabilidad = await db.Contabilidades.FindAsync(id);
            if (contabilidad == null)
            {
                return NotFound();
            }

            db.Contabilidades.Remove(contabilidad);
            await db.SaveChangesAsync();

            return Ok(contabilidad);
        }


        // POST: api/Contabilidades
        [HttpPost]
        [Route("api/Contabilidades/PuntearPorImporte")]
        [ResponseType(typeof(bool))]
        public async Task<IHttpActionResult> PuntearPorImporte([FromBody] dynamic datosPunteo)
        {
            string empresa = datosPunteo.Empresa;
            string cuenta = datosPunteo.Cuenta;
            decimal importe = datosPunteo.Importe;

            var _servicio = new ContabilidadService();
            var _gestor = new GestorContabilidad(_servicio);

            bool resultado = await _gestor.PuntearPorImporte(empresa, cuenta, importe);

            return Ok(resultado);
        }


        // Método auxiliar para obtener el estado de punteo
        private EstadoPunteo ObtenerEstadoPunteo(int contabilidadId, decimal importeContabilidad)
        {
            // Lógica para determinar el estado de punteo según tus criterios
            // Puedes utilizar consultas a la base de datos o lógica en memoria según sea necesario
            // Aquí proporciono un ejemplo simple para ilustrar el concepto, ajusta según tus necesidades.
            var totalPunteado = db.ConciliacionesBancariasPunteos
                .Where(p => p.ApunteContabilidadId == contabilidadId)
                .Select(p => p.ImportePunteado)
                .DefaultIfEmpty(0)
                .Sum();
            return totalPunteado == 0 ? EstadoPunteo.SinPuntear :
                   totalPunteado == importeContabilidad ? EstadoPunteo.CompletamentePunteado :
                   EstadoPunteo.ParcialmentePunteado;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool ContabilidadExists(string id)
        {
            return db.Contabilidades.Count(e => e.Empresa == id) > 0;
        }
    }
}