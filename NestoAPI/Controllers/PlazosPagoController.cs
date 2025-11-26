using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using System.Web.Http.Results;
using NestoAPI.Models;

namespace NestoAPI.Controllers
{
    [RoutePrefix("api/PlazosPago")]
    public class PlazosPagoController : ApiController
    {
        private NVEntities db;

        // Carlos 06/11/15: lo pongo para desactivar el Lazy Loading
        public PlazosPagoController() : this(new NVEntities())
        {
        }

        // Constructor interno para testing (inyección de dependencias)
        internal PlazosPagoController(NVEntities dbContext)
        {
            db = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            db.Configuration.LazyLoadingEnabled = false;
        }

        // GET: api/PlazosPago
        //public IQueryable<PlazoPago> GetPlazosPago(string empresa)
        //{
        //    return db.PlazosPago.Where(p => p.Empresa == empresa);
        //}

        [ResponseType(typeof(PlazoPagoDTO))]
        public async Task<IHttpActionResult> GetPlazosPago(string empresa)
        //public IQueryable<FormaPago> GetFormasPago(string empresa)
        {
            List<PlazoPagoDTO> plazosPago = await db.PlazosPago.Where(l => l.Empresa == empresa).
                Select(p => new PlazoPagoDTO
                {
                    plazoPago = p.Número.Trim(),
                    descripcion = p.Descripción.Trim(),
                    numeroPlazos = p.Nº_Plazos,
                    diasPrimerPlazo = p.DíasPrimerPlazo,
                    diasEntrePlazos = p.DíasEntrePlazos,
                    mesesPrimerPlazo = p.MesesPrimerPlazo,
                    mesesEntrePlazos = p.MesesEntrePlazos,
                    descuentoPP = p.DtoProntoPago,
                    financiacion = p.Financiacion
                }).ToListAsync();

            return Ok(plazosPago);
        }


        [ResponseType(typeof(PlazoPagoDTO))]
        public async Task<IHttpActionResult> GetPlazosPago(string empresa, string cliente)
        //public IQueryable<FormaPago> GetFormasPago(string empresa)
        {
            Cliente clienteBuscado = db.Clientes.Include(p => p.CondPagoClientes).Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.ClientePrincipal == true).SingleOrDefault();

            List<PlazoPagoDTO> plazosPago = await db.PlazosPago.Where(l => l.Empresa == empresa).
                Select(p => new PlazoPagoDTO
                {
                    plazoPago = p.Número.Trim(),
                    descripcion = p.Descripción.Trim(),
                    numeroPlazos = p.Nº_Plazos,
                    diasPrimerPlazo = p.DíasPrimerPlazo,
                    diasEntrePlazos = p.DíasEntrePlazos,
                    mesesPrimerPlazo = p.MesesPrimerPlazo,
                    mesesEntrePlazos = p.MesesEntrePlazos,
                    descuentoPP = p.DtoProntoPago,
                    financiacion = p.Financiacion
                }).ToListAsync();

            // Si el cliente es CR no se puede poner otro plazo de pago
            if (clienteBuscado == null || clienteBuscado.CondPagoClientes.Where(c => c.PlazosPago.Trim() == Constantes.PlazosPago.CONTADO_RIGUROSO).Any()) {
                plazosPago = plazosPago.Where(p => p.plazoPago == Constantes.PlazosPago.CONTADO_RIGUROSO || p.plazoPago == Constantes.PlazosPago.PREPAGO).ToList();
                return Ok(plazosPago);
            } 

            // Si el cliente tiene impagados o facturas vencidas, solo admitimos formas de pago de contado
            DateTime fechaDesdeVencida = System.DateTime.Today; // Vencidas son las de ayer o antes (FechaVto < Today)
            ExtractoCliente impagado = db.ExtractosCliente.Where(e => e.Número == cliente && e.ImportePdte > 0 && e.TipoApunte == Constantes.ExtractosCliente.TiposApunte.IMPAGADO).FirstOrDefault();
            ExtractoCliente deudaVencida = db.ExtractosCliente.Where(e => e.Número == cliente && e.ImportePdte > 0 && e.FechaVto < fechaDesdeVencida).FirstOrDefault();

            if (impagado != null || deudaVencida != null)
            {
                plazosPago = plazosPago.Where(p => p.diasPrimerPlazo == 0 && p.mesesPrimerPlazo == 0 && p.numeroPlazos == 1).ToList();
                return Ok(plazosPago);
            }

            bool tuvoImpagados = db.ExtractosCliente.Where(e => e.Número == cliente && e.TipoApunte == Constantes.ExtractosCliente.TiposApunte.IMPAGADO).Any();
            bool tieneSuficientesRecibos = db.ExtractosCliente.Where(e => e.Número == cliente && e.TipoApunte == Constantes.ExtractosCliente.TiposApunte.PAGO && e.CCC != null).Take(10).Count() == 10;
            DateTime haceUnAnno = DateTime.Today.AddYears(-1);
            bool tieneSuficienteAntiguedad = db.ExtractosCliente.Where(e => e.Número == cliente && e.TipoApunte == Constantes.ExtractosCliente.TiposApunte.PAGO && e.CCC != null && e.Fecha > haceUnAnno).Any();

            // Si el cliente nunca ha tenido un impagado, nos compra hace tiempo y ya se le han girado varios recibos, dejamos financiar hasta 6 meses
            // En estos dos últimos casos aceptamos cualquier forma de pago que haya en la ficha del cliente, para dar flexibilidad y que se puedan hacer excepciones
            if (!tuvoImpagados && tieneSuficienteAntiguedad && tieneSuficientesRecibos)
            {
                plazosPago = plazosPago.Where(p => (p.numeroPlazos <= 6 && p.financiacion <= 105) || clienteBuscado.CondPagoClientes.Select(c => c.PlazosPago?.Trim()).Contains(p.plazoPago)).ToList();
                return Ok(plazosPago);
            }
            else
            {
                plazosPago = plazosPago.Where(p => (p.numeroPlazos <= 3 && p.financiacion <= 60) || clienteBuscado.CondPagoClientes.Select(c => c.PlazosPago?.Trim()).Contains(p.plazoPago)).ToList();
                return Ok(plazosPago);
            }
        }

        [ResponseType(typeof(PlazoPagoDTO))]
        public async Task<IHttpActionResult> GetPlazosPago(string empresa, string cliente, string formaPago, decimal totalPedido)
        {
            Cliente clienteBuscado = db.Clientes.Include(p => p.CondPagoClientes).Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.ClientePrincipal == true).SingleOrDefault();

            IHttpActionResult result = await GetPlazosPago(empresa, cliente).ConfigureAwait(false);
            
            if (!(result is OkNegotiatedContentResult<List<PlazoPagoDTO>> okResult))
            {
                return BadRequest("La solicitud es incorrecta o incompleta.");
            }

            var plazosPago = okResult.Content;
            var plazosPagoCliente = clienteBuscado.CondPagoClientes.Where(c => c.ImporteMínimo <= totalPedido || (totalPedido <= 0 && c.ImporteMínimo == 0)).ToList();

            if (formaPago == Constantes.FormasPago.EFECTIVO)
            {
                plazosPago = plazosPago.Where(p => p.numeroPlazos == 1 && p.diasPrimerPlazo == 0 && p.mesesPrimerPlazo == 0 || plazosPagoCliente.Any(c => c.PlazosPago?.Trim() == p.plazoPago)).ToList();
            }

            try
            {
                plazosPago = plazosPago
                .Where(p => p.numeroPlazos == 1 ||
                    ((p.diasPrimerPlazo + p.mesesPrimerPlazo > 0) ?
                    totalPedido / p.numeroPlazos >= 100 :
                    totalPedido / (p.numeroPlazos - 1) >= 100)
                    || (plazosPagoCliente.Any(c => c.ImporteMínimo <= totalPedido && c.PlazosPago.Trim() == p.plazoPago))
                    )
                .ToList();
            } catch (Exception ex)
            {

            }
            
                        

            return Ok(plazosPago);
        }

        [HttpGet]
        [Route("ConInfoDeuda")]
        [ResponseType(typeof(PlazosPagoResponse))]
        public async Task<IHttpActionResult> GetPlazosPagoConInfoDeuda(string empresa, string cliente)
        {
            // Reutiliza la lógica existente obteniendo el resultado
            IHttpActionResult result = await GetPlazosPago(empresa, cliente);

            if (!(result is OkNegotiatedContentResult<List<PlazoPagoDTO>> okResult))
            {
                return result; // Si hubo error, lo propagamos
            }

            var plazosPago = okResult.Content;
            var infoDeuda = ObtenerInfoDeuda(cliente);

            // Si el cliente tiene deuda vencida o impagados, recomendar PRE (prepago)
            // En caso contrario, no hay recomendación específica (el frontend usará el valor del cliente)
            string plazoPagoRecomendado = null;
            if (infoDeuda.TieneImpagados || infoDeuda.TieneDeudaVencida)
            {
                plazoPagoRecomendado = Constantes.PlazosPago.PREPAGO;
            }

            return Ok(new PlazosPagoResponse
            {
                PlazosPago = plazosPago,
                InfoDeuda = infoDeuda,
                PlazoPagoRecomendado = plazoPagoRecomendado
            });
        }

        [HttpGet]
        [Route("CondicionesPago")]
        [ResponseType(typeof(CondicionesPagoResponse))]
        public async Task<IHttpActionResult> GetCondicionesPago(string empresa, string cliente)
        {
            // Obtener plazos de pago
            IHttpActionResult resultPlazos = await GetPlazosPago(empresa, cliente);
            if (!(resultPlazos is OkNegotiatedContentResult<List<PlazoPagoDTO>> okPlazos))
            {
                return resultPlazos;
            }
            var plazosPago = okPlazos.Content;

            // Obtener info de deuda
            var infoDeuda = ObtenerInfoDeuda(cliente);

            // Obtener formas de pago y filtrar según deuda
            var formasPago = await ObtenerFormasPagoFiltradas(empresa, cliente, infoDeuda);

            // Determinar recomendaciones
            string plazoPagoRecomendado = null;
            string formaPagoRecomendada = null;

            if (infoDeuda.TieneImpagados || infoDeuda.TieneDeudaVencida)
            {
                plazoPagoRecomendado = Constantes.PlazosPago.PREPAGO;
                // Recomendar efectivo como primera opción segura
                formaPagoRecomendada = Constantes.FormasPago.EFECTIVO;
            }

            return Ok(new CondicionesPagoResponse
            {
                PlazosPago = plazosPago,
                FormasPago = formasPago,
                InfoDeuda = infoDeuda,
                PlazoPagoRecomendado = plazoPagoRecomendado,
                FormaPagoRecomendada = formaPagoRecomendada
            });
        }

        private async Task<List<FormaPagoDTO>> ObtenerFormasPagoFiltradas(string empresa, string cliente, InfoDeudaClienteDTO infoDeuda)
        {
            // Cargar todas las formas de pago estándar
            var formasPago = await db.FormasPago
                .Where(l => l.Empresa == empresa && !l.BloquearPagos && l.Número != "TAL")
                .Select(f => new FormaPagoDTO
                {
                    formaPago = f.Número.Trim(),
                    descripcion = f.Descripción.Trim(),
                    bloquearPagos = f.BloquearPagos,
                    cccObligatorio = f.CCCObligatorio
                })
                .ToListAsync();

            // Si el cliente no tiene ningún CCC, quitar las formas de pago que requieran CCC
            CCC ccc = db.CCCs.Where(c => c.Empresa == empresa && c.Cliente == cliente && c.Estado >= 0).FirstOrDefault();
            if (ccc == null)
            {
                formasPago = formasPago.Where(f => f.cccObligatorio == false).ToList();
            }

            // Si el cliente tiene deuda (impagados o vencida), solo permitir formas de pago seguras
            if (infoDeuda.TieneImpagados || infoDeuda.TieneDeudaVencida)
            {
                formasPago = formasPago
                    .Where(f => Constantes.FormasPago.FORMAS_PAGO_SEGURAS.Contains(f.formaPago))
                    .ToList();
            }

            return formasPago;
        }

        /*
        // GET: api/PlazosPago/5
        [ResponseType(typeof(PlazoPago))]
        public async Task<IHttpActionResult> GetPlazoPago(string id)
        {
            PlazoPago plazoPago = await db.PlazosPago.FindAsync(id);
            if (plazoPago == null)
            {
                return NotFound();
            }

            return Ok(plazoPago);
        }

        // PUT: api/PlazosPago/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutPlazoPago(string id, PlazoPago plazoPago)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != plazoPago.Empresa)
            {
                return BadRequest();
            }

            db.Entry(plazoPago).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!PlazoPagoExists(id))
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

        // POST: api/PlazosPago
        [ResponseType(typeof(PlazoPago))]
        public async Task<IHttpActionResult> PostPlazoPago(PlazoPago plazoPago)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.PlazosPago.Add(plazoPago);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (PlazoPagoExists(plazoPago.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = plazoPago.Empresa }, plazoPago);
        }

        // DELETE: api/PlazosPago/5
        [ResponseType(typeof(PlazoPago))]
        public async Task<IHttpActionResult> DeletePlazoPago(string id)
        {
            PlazoPago plazoPago = await db.PlazosPago.FindAsync(id);
            if (plazoPago == null)
            {
                return NotFound();
            }

            db.PlazosPago.Remove(plazoPago);
            await db.SaveChangesAsync();

            return Ok(plazoPago);
        }
        */

        internal InfoDeudaClienteDTO ObtenerInfoDeuda(string cliente)
        {
            DateTime fechaDesdeVencida = System.DateTime.Today; // Vencidas son las de ayer o antes (FechaVto < Today)

            // Buscar impagados
            var impagados = db.ExtractosCliente
                .Where(e => e.Número == cliente
                    && e.ImportePdte > 0
                    && e.TipoApunte == Constantes.ExtractosCliente.TiposApunte.IMPAGADO)
                .ToList();

            // Buscar cartera vencida (excluyendo impagados para evitar doble contabilización)
            var deudasVencidas = db.ExtractosCliente
                .Where(e => e.Número == cliente
                    && e.ImportePdte > 0
                    && e.FechaVto < fechaDesdeVencida
                    && e.TipoApunte != Constantes.ExtractosCliente.TiposApunte.IMPAGADO)
                .OrderBy(e => e.FechaVto)
                .ToList();

            bool tieneImpagados = impagados.Any();
            bool tieneDeudaVencida = deudasVencidas.Any();
            decimal? importeImpagados = tieneImpagados ? impagados.Sum(e => e.ImportePdte) : (decimal?)null;
            decimal? importeDeudaVencida = tieneDeudaVencida ? deudasVencidas.Sum(e => e.ImportePdte) : (decimal?)null;
            int? diasVencimiento = null;
            string motivoRestriccion = null;

            if (tieneDeudaVencida)
            {
                var deudaMasAntigua = deudasVencidas.First();
                diasVencimiento = (DateTime.Today - deudaMasAntigua.FechaVto)?.Days;
                motivoRestriccion = "Cartera vencida";
            }

            if (tieneImpagados)
            {
                motivoRestriccion = tieneDeudaVencida ? "Impagados y cartera vencida" : "Impagados";
            }

            return new InfoDeudaClienteDTO
            {
                TieneDeudaVencida = tieneDeudaVencida,
                ImporteDeudaVencida = importeDeudaVencida,
                DiasVencimiento = diasVencimiento,
                TieneImpagados = tieneImpagados,
                ImporteImpagados = importeImpagados,
                MotivoRestriccion = motivoRestriccion
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool PlazoPagoExists(string id)
        {
            return db.PlazosPago.Count(e => e.Empresa == id) > 0;
        }
    }
}