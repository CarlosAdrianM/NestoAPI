using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;
using NestoAPI.Models.ApuntesBanco;
using NestoAPI.Models.Mayor;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class ContabilidadesController : ApiController
    {
        private readonly NVEntities db = new NVEntities();

        public ContabilidadesController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }

        //// GET: api/Contabilidades
        //public IQueryable<Contabilidad> GetContabilidades()
        //{
        //    return db.Contabilidades;
        //}

        // GET: api/Contabilidades/5
        [ResponseType(typeof(Contabilidad))]
        public async Task<IHttpActionResult> GetContabilidad(string id)
        {
            Contabilidad contabilidad = await db.Contabilidades.FindAsync(id);
            return contabilidad == null ? NotFound() : (IHttpActionResult)Ok(contabilidad);
        }

        // GET: api/Contabilidades/5
        [ResponseType(typeof(List<ContabilidadDTO>))]
        public async Task<IHttpActionResult> GetContabilidad(string cuenta, bool punteado)
        {
            try
            {
                // Realizar la consulta y proyección
                List<ContabilidadDTO> apuntesDTO = await db.Contabilidades
                    .Where(c => c.Nº_Cuenta == cuenta && c.Punteado == punteado && c.Diario != "_ASIENTCIE")
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

                return apuntesDTO == null ? NotFound() : (IHttpActionResult)Ok(apuntesDTO);
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
            DateTime fechaDiaSiguiente = new DateTime(fecha.Year, fecha.Month, fecha.Day).AddDays(1);


            try
            {
                IQueryable<Contabilidad> contabilidadEmpresa = string.IsNullOrEmpty(empresa)
                    ? db.Contabilidades
                    : db.Contabilidades
                    .Where(c => c.Empresa == empresa);
                DateTime? fechaUltimoCierre = await contabilidadEmpresa
                .Where(c => c.Diario == Constantes.Contabilidad.Diarios.DIARIO_CIERRE && c.Fecha <= fecha)
                .Select(c => DbFunctions.TruncateTime(c.Fecha)) // para que no coja la hora, minutos, ni segundos
                .MaxAsync();

                decimal saldo = await contabilidadEmpresa
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
            DateTime fechaHastaMenor = fechaHasta.AddDays(1);
            try
            {
                IQueryable<Contabilidad> contabilidadEmpresa = string.IsNullOrEmpty(empresa)
                    ? db.Contabilidades
                    : db.Contabilidades
                    .Where(c => c.Empresa == empresa);

                // Realizar la consulta y proyección
                List<ContabilidadDTO> apuntesDTO = await contabilidadEmpresa
                    .Where(c => c.Nº_Cuenta == cuenta && c.Fecha >= fechaDesde && c.Fecha < fechaHastaMenor && c.Diario != "_ASIENTCIE")
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

                foreach (ContabilidadDTO apunte in apuntesDTO)
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
                IQueryable<Contabilidad> consultaInicial = db.Contabilidades
                .Where(c => c.Empresa == empresa && c.Concepto.Contains(concepto) &&
                        c.Fecha >= fechaDesde && c.Fecha <= fechaHasta && c.Nº_Cuenta.StartsWith("6") &&
                        !db.ExtractosProveedor.Any(e => e.Empresa == c.Empresa && e.Asiento == c.Asiento)
                );

                List<ContabilidadDTO> resultado = await consultaInicial
                    .GroupBy(c => new
                    {
                        Cuenta = c.Nº_Cuenta.Trim(),
                        Delegacion = c.Delegación,
                        c.Departamento,
                        c.CentroCoste
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

        /// <summary>
        /// Genera un PDF con el Mayor de una cuenta (cliente o proveedor).
        /// </summary>
        /// <param name="empresa">Codigo de empresa</param>
        /// <param name="tipoCuenta">"cliente" o "proveedor"</param>
        /// <param name="cuenta">Numero de cliente o proveedor</param>
        /// <param name="fechaDesde">Fecha inicio del periodo (opcional, por defecto inicio del año)</param>
        /// <param name="fechaHasta">Fecha fin del periodo (opcional, por defecto hoy)</param>
        /// <param name="soloFacturas">Si es true, solo muestra movimientos con TipoApunte = 1 (facturas)</param>
        /// <param name="eliminarPasoACartera">Si es true, elimina los pares Factura+PasoACartera que se anulan</param>
        /// <returns>PDF con el Mayor de la cuenta</returns>
        [HttpGet]
        [Authorize]
        [Route("api/Contabilidades/MayorPdf")]
        public async Task<HttpResponseMessage> GetMayorPdf(
            string empresa,
            string tipoCuenta,
            string cuenta,
            DateTime? fechaDesde = null,
            DateTime? fechaHasta = null,
            bool soloFacturas = false,
            bool eliminarPasoACartera = false)
        {
            // Valores por defecto para fechas
            DateTime desde = fechaDesde ?? new DateTime(DateTime.Now.Year, 1, 1);
            DateTime hasta = fechaHasta ?? DateTime.Now;
            DateTime hastaMasUno = hasta.AddDays(1);

            // Validar parametros
            if (string.IsNullOrWhiteSpace(empresa) || string.IsNullOrWhiteSpace(tipoCuenta) || string.IsNullOrWhiteSpace(cuenta))
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, "Empresa, tipoCuenta y cuenta son obligatorios");
            }

            tipoCuenta = tipoCuenta.ToLower().Trim();
            if (tipoCuenta != "cliente" && tipoCuenta != "proveedor")
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, "tipoCuenta debe ser 'cliente' o 'proveedor'");
            }

            try
            {
                // Obtener datos de la empresa
                Empresa empresaEntity = await db.Empresas.FirstOrDefaultAsync(e => e.Número == empresa);
                if (empresaEntity == null)
                {
                    return Request.CreateErrorResponse(HttpStatusCode.NotFound, "Empresa no encontrada");
                }

                // Crear DTO del Mayor
                MayorCuentaDTO mayorDTO = new MayorCuentaDTO
                {
                    Empresa = empresa,
                    NombreEmpresa = empresaEntity.Nombre?.Trim(),
                    TipoCuenta = tipoCuenta,
                    NumeroCuenta = cuenta,
                    FechaDesde = desde,
                    FechaHasta = hasta,
                    SoloFacturas = soloFacturas
                };

                List<MovimientoMayorDTO> movimientos;

                if (tipoCuenta == "cliente")
                {
                    // Obtener datos del cliente
                    Cliente cliente = await db.Clientes
                        .FirstOrDefaultAsync(c => c.Empresa == empresa && c.Nº_Cliente == cuenta);
                    if (cliente == null)
                    {
                        return Request.CreateErrorResponse(HttpStatusCode.NotFound, "Cliente no encontrado");
                    }

                    mayorDTO.NombreCuenta = cliente.Nombre?.Trim();
                    mayorDTO.CifNif = cliente.CIF_NIF?.Trim();
                    mayorDTO.Direccion = $"{cliente.Dirección?.Trim()}, {cliente.CodPostal?.Trim()} {cliente.Población?.Trim()}";

                    // Calcular saldo anterior (para clientes no necesita conversion)
                    decimal saldoAnterior = await db.ExtractosCliente
                        .Where(e => e.Empresa == empresa && e.Número == cuenta && e.Fecha < desde)
                        .Select(e => e.Importe)
                        .DefaultIfEmpty(0)
                        .SumAsync();
                    // Para clientes: positivo en ExtractoCliente = Debe (nos deben)
                    mayorDTO.SaldoAnterior = saldoAnterior;

                    // Obtener extractos del periodo
                    IQueryable<ExtractoCliente> query = db.ExtractosCliente
                        .Where(e => e.Empresa == empresa && e.Número == cuenta &&
                                   e.Fecha >= desde && e.Fecha < hastaMasUno);

                    // Filtrar por TipoApunte si soloFacturas
                    if (soloFacturas)
                    {
                        query = query.Where(e => e.TipoApunte == Constantes.TiposExtractoCliente.FACTURA);
                    }

                    List<ExtractoCliente> extractos = await query
                        .OrderBy(e => e.Fecha)
                        .ThenBy(e => e.Nº_Orden)
                        .ToListAsync();

                    // Adaptar a MovimientoMayorDTO
                    ExtractoClienteAdapter adapter = new ExtractoClienteAdapter();
                    movimientos = adapter.Adaptar(extractos).ToList();
                }
                else // proveedor
                {
                    // Obtener datos del proveedor
                    Proveedor proveedor = await db.Proveedores
                        .FirstOrDefaultAsync(p => p.Empresa == empresa && p.Número == cuenta);
                    if (proveedor == null)
                    {
                        return Request.CreateErrorResponse(HttpStatusCode.NotFound, "Proveedor no encontrado");
                    }

                    mayorDTO.NombreCuenta = proveedor.Nombre?.Trim();
                    mayorDTO.CifNif = proveedor.CIF_NIF?.Trim();
                    mayorDTO.Direccion = $"{proveedor.Dirección?.Trim()}, {proveedor.CodPostal?.Trim()} {proveedor.Población?.Trim()}";

                    // Calcular saldo anterior (para proveedores el signo es inverso)
                    decimal sumatoriaImportes = await db.ExtractosProveedor
                        .Where(e => e.Empresa == empresa && e.Número == cuenta && e.Fecha < desde)
                        .Select(e => e.Importe)
                        .DefaultIfEmpty(0)
                        .SumAsync();
                    // Convertir saldo de proveedor: positivo en ExtractoProveedor = Haber (debemos)
                    // Para el calculo Debe-Haber, necesitamos invertir el signo
                    mayorDTO.SaldoAnterior = CalculadorSaldoMayor.ConvertirSaldoAnteriorProveedor(sumatoriaImportes);

                    // Obtener extractos del periodo
                    IQueryable<ExtractoProveedor> query = db.ExtractosProveedor
                        .Where(e => e.Empresa == empresa && e.Número == cuenta &&
                                   e.Fecha >= desde && e.Fecha < hastaMasUno);

                    // Filtrar por TipoApunte si soloFacturas
                    if (soloFacturas)
                    {
                        query = query.Where(e => e.TipoApunte == Constantes.TiposExtractoCliente.FACTURA);
                    }

                    List<ExtractoProveedor> extractos = await query
                        .OrderBy(e => e.Fecha)
                        .ThenBy(e => e.NºOrden)
                        .ToListAsync();

                    // Adaptar a MovimientoMayorDTO
                    ExtractoProveedorAdapter adapter = new ExtractoProveedorAdapter();
                    movimientos = adapter.Adaptar(extractos).ToList();
                }

                // Aplicar filtro de paso a cartera si se solicita
                if (eliminarPasoACartera)
                {
                    movimientos = CalculadorSaldoMayor.EliminarPasoACartera(movimientos);
                }

                // Calcular saldos usando el helper
                var (totalDebe, totalHaber, saldoFinal) = CalculadorSaldoMayor.CalcularSaldos(movimientos, mayorDTO.SaldoAnterior);

                mayorDTO.Movimientos = movimientos;
                mayorDTO.TotalDebe = totalDebe;
                mayorDTO.TotalHaber = totalHaber;
                mayorDTO.SaldoFinal = saldoFinal;
                mayorDTO.EliminarPasoACartera = eliminarPasoACartera;

                // Generar PDF
                GeneradorPdfMayor generador = new GeneradorPdfMayor();
                byte[] pdfBytes = generador.Generar(mayorDTO, empresaEntity);

                // Crear respuesta
                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(pdfBytes)
                };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline")
                {
                    FileName = $"Mayor_{tipoCuenta}_{cuenta}_{desde:yyyyMMdd}_{hasta:yyyyMMdd}.pdf"
                };

                return response;
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
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
                _ = await db.SaveChangesAsync();
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

            _ = db.Contabilidades.Add(contabilidad);

            try
            {
                _ = await db.SaveChangesAsync();
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

            _ = db.Contabilidades.Remove(contabilidad);
            _ = await db.SaveChangesAsync();

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

            ContabilidadService _servicio = new ContabilidadService();
            GestorContabilidad _gestor = new GestorContabilidad(_servicio);

            bool resultado = await _gestor.PuntearPorImporte(empresa, cuenta, importe);

            return Ok(resultado);
        }


        // Método auxiliar para obtener el estado de punteo
        private EstadoPunteo ObtenerEstadoPunteo(int contabilidadId, decimal importeContabilidad)
        {
            // Lógica para determinar el estado de punteo según tus criterios
            // Puedes utilizar consultas a la base de datos o lógica en memoria según sea necesario
            // Aquí proporciono un ejemplo simple para ilustrar el concepto, ajusta según tus necesidades.
            decimal totalPunteado = db.ConciliacionesBancariasPunteos
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