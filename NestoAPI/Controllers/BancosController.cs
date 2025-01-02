using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Core.Objects;
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
using NestoAPI.Models.Bancos;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Controllers
{
    public class BancosController : ApiController
    {
        private NVEntities db = new NVEntities();
        
        public BancosController() {
            db.Configuration.LazyLoadingEnabled = false;
        }


        // GET: api/Bancos
        public IQueryable<Banco> GetBancos()
        {
            return db.Bancos;
        }
        // GET: api/Bancos/5
        [ResponseType(typeof(Banco))]
        public async Task<IHttpActionResult> GetBanco(string empresa, string codigoBanco)
        {
            Banco banco = await db.Bancos.SingleAsync(b => b.Empresa == empresa && b.Número == codigoBanco);
            if (banco == null)
            {
                return NotFound();
            }

            return Ok(banco);
        }

        // GET: api/Bancos/5
        [ResponseType(typeof(Banco))]
        public async Task<IHttpActionResult> GetBanco(string entidad, string oficina, string cuenta)
        {
            Banco banco = await db.Bancos.SingleAsync(b => b.Entidad == entidad && b.Sucursal == oficina && b.Nº_Cuenta == cuenta);
            if (banco == null)
            {
                return NotFound();
            }

            return Ok(banco);
        }

        // GET: api/Banco/5
        [ResponseType(typeof(List<ApunteBancarioDTO>))]
        public async Task<IHttpActionResult> GetBanco(string empresa, string codigoBanco, DateTime fechaDesde, DateTime fechaHasta)
        {
            var fechaHastaMenor = fechaHasta.AddDays(1);
            try
            {
                Banco banco = db.Bancos.Where(b => b.Empresa == empresa && b.Número == codigoBanco).Single();
                if (banco == null)
                {
                    return NotFound();
                }

                // Filtrar FicherosCuaderno43 por entidad, oficina y número de cuenta
                IQueryable<ApunteBancarioDTO> apuntesQuery = db.FicherosCuaderno43
                    .Where(f => f.ClaveEntidad == banco.Entidad && f.ClaveOficina == banco.Sucursal && f.NumeroCuenta == banco.Nº_Cuenta)
                    .SelectMany(f => f.ApuntesBancarios)
                    .Where(a => a.FechaOperacion >= fechaDesde && a.FechaOperacion < fechaHastaMenor)
                    .Select(a => new ApunteBancarioDTO
                    {
                        Id = a.Id,
                        ClaveOficinaOrigen = a.ClaveOficinaOrigen,
                        FechaOperacion = a.FechaOperacion,
                        FechaValor = a.FechaValor,
                        ConceptoComun = a.ConceptoComun,
                        ConceptoPropio = a.ConceptoPropio,
                        ClaveDebeOHaberMovimiento = a.ClaveDebeOHaberMovimiento,
                        ImporteMovimiento = a.ImporteMovimiento,
                        NumeroDocumento = a.NumeroDocumento,
                        Referencia1 = a.Referencia1,
                        Referencia2 = a.Referencia2,                        
                        // Mapeo de relaciones
                        RegistrosConcepto = a.RegistroComplementarioConceptoes.Select(rc => new ConceptoComplementario
                        {
                            CodigoDatoConcepto = rc.CodigoDato,
                            Concepto = rc.Concepto,
                            Concepto2 = rc.Concepto2
                        }).ToList(),

                        ImporteEquivalencia = a.RegistroComplementarioEquivalencias.Select(re => new EquivalenciaDivisas
                        {
                            CodigoDatoEquivalencia = re.CodigoDato,
                            ClaveDivisaOrigen = re.ClaveDivisaOrigen,
                            ImporteEquivalencia = re.ImporteEquivalencia,
                        }).FirstOrDefault()
                    });

                List<ApunteBancarioDTO> apuntesBancariosDTO = await apuntesQuery.ToListAsync();

                // Realizar la conversión después de obtener los resultados
                foreach (var apunte in apuntesBancariosDTO)
                {
                    apunte.TextoConceptoComun = GestorContabilidad.ObtenerTextoConceptoComun(apunte.ConceptoComun);
                    apunte.EstadoPunteo = db.ConciliacionesBancariasPunteos
                            .Where(p => p.ApunteBancoId == apunte.Id)
                            .Select(p => p.ImportePunteado)
                            .DefaultIfEmpty(0)
                            .Sum() == apunte.ImporteConSigno ? EstadoPunteo.CompletamentePunteado :
                                db.ConciliacionesBancariasPunteos
                                    .Where(p => p.ApunteBancoId == apunte.Id)
                                    .Select(p => p.ImportePunteado)
                                    .DefaultIfEmpty(0)
                                    .Sum() != 0 ? EstadoPunteo.ParcialmentePunteado : EstadoPunteo.SinPuntear;
                }

                return Ok(apuntesBancariosDTO.OrderBy(a => a.FechaOperacion).ThenBy(a => a.Id));
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /*
        // PUT: api/Bancos/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutBanco(string id, Banco banco)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != banco.Empresa)
            {
                return BadRequest();
            }

            db.Entry(banco).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!BancoExists(id))
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
        

        // POST: api/Bancos/CargarFichero
        [HttpPost]
        [Route("api/Bancos/CargarFichero")]
        [ResponseType(typeof(ContenidoCuaderno43))]
        public async Task<IHttpActionResult> CargarFichero([FromBody] dynamic contenidoUsuario)
        {
            // Acceder a los datos del tipo anónimo
            string contenido = contenidoUsuario.Contenido;
            string usuario = contenidoUsuario.Usuario;

            var apuntes = await GestorContabilidad.LeerCuaderno43(contenido);
            apuntes.Usuario = usuario;
            var servicio = new ContabilidadService();
            var gestorContabilidad = new GestorContabilidad(servicio);
            await gestorContabilidad.PersistirCuaderno43(apuntes);
            return Ok(apuntes);
        }

        // POST: api/Bancos/CargarFichero
        [HttpPost]
        [Route("api/Bancos/CargarFicheroTarjetas")]
        [ResponseType(typeof(List<MovimientoTPVDTO>))]
        public async Task<IHttpActionResult> CargarFicheroTarjetas([FromBody] dynamic contenidoUsuario)
        {
            // Acceder a los datos del tipo anónimo
            string contenido = contenidoUsuario.Contenido;
            string usuario = contenidoUsuario.Usuario;

            var movimientosTPV = GestorContabilidad.LeerMovimientosTPV(contenido, usuario);
            if (movimientosTPV != null && movimientosTPV.Any())
            {
                var servicio = new ContabilidadService();
                var gestorContabilidad = new GestorContabilidad(servicio);
                await gestorContabilidad.PersistirMovimientosTPV(movimientosTPV);
                await gestorContabilidad.ContabilizarComisionesTarjetas(movimientosTPV);
            }

            return Ok(movimientosTPV);
        }


        [HttpGet]
        [Route("api/Bancos/LeerPrepagoPendientePorImporte")]
        [ResponseType(typeof(List<Prepago>))]
        public async Task<IHttpActionResult> LeerPrepagoPendientePorImporte(decimal importe)
        {
            var prepagos = db.Prepagos.Where(p => p.Factura == null && p.Importe == importe);            
            return Ok(prepagos);
        }


        [HttpGet]
        [Route("api/Bancos/LeerMovimientosTPV")]
        [ResponseType(typeof(List<MovimientoTPVDTO>))]
        public async Task<IHttpActionResult> LeerMovimientosTPV(DateTime fechaCaptura, string tipoDatafono)
        {
            var servicio = new ContabilidadService();
            var movimientos = await servicio.LeerMovimientosTPV(fechaCaptura, tipoDatafono);
            return Ok(movimientos);
        }

        [HttpGet]
        [Route("api/Bancos/LeerProveedorPorNif")]
        [ResponseType(typeof(string))]
        public async Task<IHttpActionResult> LeerProveedorPorNif(string nifProveedor)
        {
            var servicio = new ContabilidadService();
            var proveedor = await servicio.LeerProveedorPorNif(nifProveedor);
            return Ok(proveedor);
        }

        [HttpGet]
        [Route("api/Bancos/LeerProveedorPorNombre")]
        [ResponseType(typeof(string))]
        public async Task<IHttpActionResult> LeerProveedorPorNombre(string nombreProveedor)
        {
            var servicio = new ContabilidadService();
            var proveedor = await servicio.LeerProveedorPorNombre(nombreProveedor);
            return Ok(proveedor);
        }

        [HttpGet]
        [Route("api/Bancos/PagoPendienteUnico")]
        [ResponseType(typeof(ExtractoProveedorDTO))]
        public async Task<IHttpActionResult> PagoPendienteUnico(string proveedor, decimal importe)
        {
            var servicio = new ContabilidadService();
            var pagoPendiente = await servicio.PagoPendienteUnico(proveedor, importe);
            return Ok(pagoPendiente);
        }

        [HttpPost]
        [Route("api/Bancos/PuntearApuntes")]
        [ResponseType(typeof(ContenidoCuaderno43))]
        public async Task<IHttpActionResult> PuntearApuntes([FromBody] dynamic datosPunteo)
        {
            // Acceder a los datos del tipo anónimo
            int? apunteBancoId = datosPunteo.ApunteBancoId;
            int? apunteContabilidadId = datosPunteo.ApunteContabilidadId;
            decimal importePunteo = datosPunteo.ImportePunteo;
            string simboloPunteo = datosPunteo.SimboloPunteo;
            int? grupoPunteo = datosPunteo.GrupoPunteo;
            string usuario = datosPunteo.Usuario;

            var punteo = await GestorContabilidad.PuntearApuntes(apunteBancoId, apunteContabilidadId, importePunteo, simboloPunteo, grupoPunteo, usuario);
            return Ok(punteo);
        }

        [HttpGet]
        [Route("api/Bancos/NumeroRecibosRemesa")]
        [ResponseType(typeof(int))]
        public async Task<IHttpActionResult> NumeroRecibosRemesa(int remesa)
        {
            var servicio = new ContabilidadService();
            var numeroRecibos = await servicio.NumeroRecibosRemesa(remesa);
            return Ok(numeroRecibos);
        }

        [HttpGet]
        [Route("api/Bancos/SaldoFinal")]
        [ResponseType(typeof(decimal))]
        public async Task<IHttpActionResult> SaldoFinal(string entidad, string oficina, string cuenta, DateTime fecha)
        {
            var servicio = new ContabilidadService();
            var saldo = await servicio.SaldoFinal(entidad, oficina, cuenta, fecha);
            return Ok(saldo);
        }

        [HttpGet]
        [Route("api/Bancos/SaldoInicial")]
        [ResponseType(typeof(decimal))]
        public async Task<IHttpActionResult> SaldoInicial(string entidad, string oficina, string cuenta, DateTime fecha)
        {
            var servicio = new ContabilidadService();
            var saldo = await servicio.SaldoInicial(entidad, oficina, cuenta, fecha);
            return Ok(saldo);
        }

        /*
        // POST: api/Bancos
        [ResponseType(typeof(Banco))]
        public async Task<IHttpActionResult> PostBanco(Banco banco)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.Bancos.Add(banco);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (BancoExists(banco.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = banco.Empresa }, banco);
        }

        // DELETE: api/Bancos/5
        [ResponseType(typeof(Banco))]
        public async Task<IHttpActionResult> DeleteBanco(string id)
        {
            Banco banco = await db.Bancos.FindAsync(id);
            if (banco == null)
            {
                return NotFound();
            }

            db.Bancos.Remove(banco);
            await db.SaveChangesAsync();

            return Ok(banco);
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

        private bool BancoExists(string empresa, string id)
        {
            return db.Bancos.Any(e => e.Empresa == empresa && e.Número == id);
        }
    }
}