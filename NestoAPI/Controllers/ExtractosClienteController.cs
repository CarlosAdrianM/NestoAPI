using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Domiciliaciones;
using NestoAPI.Models;
using NestoAPI.Models.Domiciliaciones;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class ExtractosClienteController : ApiController
    {
        private readonly NVEntities db = new NVEntities();
        private readonly IServicioCorreoElectronico _servicioCorreo;

        // Carlos 25/01/16: lo pongo para desactivar el Lazy Loading
        public ExtractosClienteController(IServicioCorreoElectronico servicioCorreo)
        {
            db.Configuration.LazyLoadingEnabled = false;
            _servicioCorreo = servicioCorreo;
        }


        // GET: api/ExtractosCliente
        public IQueryable<ExtractoClienteDTO> GetExtractosCliente(string cliente)
        {
            List<ExtractoClienteDTO> extracto = db.ExtractosCliente.Where(e => e.Número == cliente && e.ImportePdte != 0 && e.Estado != Constantes.ExtractosCliente.Estados.DEUDA_VENCIDA)
                .Select(extractoEncontrado => new ExtractoClienteDTO
                {
                    id = extractoEncontrado.Nº_Orden,
                    empresa = extractoEncontrado.Empresa.Trim(),
                    asiento = extractoEncontrado.Asiento,
                    cliente = extractoEncontrado.Número.Trim(),
                    contacto = extractoEncontrado.Contacto.Trim(),
                    fecha = extractoEncontrado.Fecha,
                    tipo = extractoEncontrado.TipoApunte.Trim(),
                    documento = extractoEncontrado.Nº_Documento.Trim(),
                    efecto = extractoEncontrado.Efecto.Trim(),
                    concepto = extractoEncontrado.Concepto.Trim(),
                    importe = extractoEncontrado.Importe,
                    importePendiente = extractoEncontrado.ImportePdte,
                    vendedor = extractoEncontrado.Vendedor.Trim(),
                    vencimiento = extractoEncontrado.FechaVto ?? extractoEncontrado.Fecha,
                    ccc = extractoEncontrado.CCC.Trim(),
                    ruta = extractoEncontrado.Ruta.Trim(),
                    estado = extractoEncontrado.Estado.Trim(),
                    formaPago = extractoEncontrado.FormaPago.Trim(),
                    delegacion = extractoEncontrado.Delegación.Trim(),
                    formaVenta = extractoEncontrado.FormaVenta.Trim(),
                    usuario = extractoEncontrado.Usuario.Trim()
                }).ToList();
            return extracto.AsQueryable();
        }

        // GET: api/ExtractosCliente
        public IQueryable<ExtractoClienteDTO> GetExtractosCliente(string empresa, string cliente, string tipoApunte, DateTime fechaDesde, DateTime fechaHasta)
        {
            IQueryable<ExtractoClienteDTO> extracto = db.ExtractosCliente.Where(e => e.Número == cliente && e.TipoApunte == tipoApunte && e.Fecha >= fechaDesde && e.Fecha <= fechaHasta).OrderBy(e => e.Fecha)
                .Select(extractoEncontrado => new ExtractoClienteDTO
                {
                    id = extractoEncontrado.Nº_Orden,
                    empresa = extractoEncontrado.Empresa.Trim(),
                    asiento = extractoEncontrado.Asiento,
                    cliente = extractoEncontrado.Número.Trim(),
                    contacto = extractoEncontrado.Contacto.Trim(),
                    fecha = extractoEncontrado.Fecha,
                    tipo = extractoEncontrado.TipoApunte.Trim(),
                    documento = extractoEncontrado.Nº_Documento.Trim(),
                    efecto = extractoEncontrado.Efecto.Trim(),
                    concepto = extractoEncontrado.Concepto.Trim(),
                    importe = extractoEncontrado.Importe,
                    importePendiente = extractoEncontrado.ImportePdte,
                    vendedor = extractoEncontrado.Vendedor.Trim(),
                    vencimiento = extractoEncontrado.FechaVto,
                    ccc = extractoEncontrado.CCC.Trim(),
                    ruta = extractoEncontrado.Ruta.Trim(),
                    estado = extractoEncontrado.Estado.Trim(),
                    formaPago = extractoEncontrado.FormaPago.Trim()
                });
            return extracto;
        }

        // GET: api/ExtractosCliente
        [Authorize]
        [ResponseType(typeof(Mod347DTO))]
        public async Task<IHttpActionResult> GetModelo347(string empresa, string cliente, string NIF)
        {
            Mod347DTO modelo = new Mod347DTO();

            Cliente clienteComprobacion = await db.Clientes.SingleOrDefaultAsync(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.ClientePrincipal == true);
            if (clienteComprobacion.CIF_NIF != null && clienteComprobacion.CIF_NIF.Trim() != NIF.Trim())
            {
                throw new Exception("El NIF no es correcto");
            }

            modelo.nombre = clienteComprobacion.Nombre != null ? clienteComprobacion.Nombre.Trim() : "";
            modelo.direccion = clienteComprobacion.Dirección != null ? clienteComprobacion.Dirección.Trim() : "";
            modelo.codigoPostal = clienteComprobacion.CodPostal != null ? clienteComprobacion.CodPostal.Trim() : "";

            DateTime hoy = System.DateTime.Today;
            DateTime fechaDesde = new DateTime(hoy.AddYears(-1).Year, 1, 1);
            DateTime fechaHasta = new DateTime(hoy.Year, 1, 1);

            List<ExtractoClienteDTO> extracto = await db.ExtractosCliente.Where(e => e.Empresa == empresa && e.Número == cliente && e.TipoApunte == "1" && e.Fecha >= fechaDesde && e.Fecha < fechaHasta && e.Estado != Constantes.ExtractosCliente.Estados.DEUDA_VENCIDA)
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
            for (int i = 0; i <= 3; i++)
            {
                modelo.trimestre[i] = extracto.Where(e => (e.fecha.Month + 2) / 3 == i + 1).Sum(e => e.importe);
            }


            modelo.MovimientosMayor = extracto;

            return Ok(modelo);
        }

        /// <summary>
        /// Descarga el certificado del Modelo 347 en formato PDF.
        /// Issue #69: Endpoint para que los clientes no tengan que generar el PDF cada uno.
        /// </summary>
        /// <param name="empresa">Código de empresa</param>
        /// <param name="cliente">Código de cliente</param>
        /// <param name="anno">Año del ejercicio (opcional, por defecto año anterior)</param>
        /// <returns>PDF del certificado del Modelo 347</returns>
        [HttpGet]
        [Authorize]
        [Route("api/ExtractosCliente/Modelo347Pdf")]
        public async Task<HttpResponseMessage> GetModelo347Pdf(string empresa, string cliente, int? anno = null)
        {
            int ejercicio = anno ?? DateTime.Today.AddYears(-1).Year;
            DateTime fechaDesde = new DateTime(ejercicio, 1, 1);
            DateTime fechaHasta = new DateTime(ejercicio + 1, 1, 1);

            // Cargar datos del cliente
            Cliente clienteDb = await db.Clientes.SingleOrDefaultAsync(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.ClientePrincipal == true);
            if (clienteDb == null)
            {
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, "Cliente no encontrado");
            }

            // Cargar datos del extracto
            List<ExtractoClienteDTO> extracto = await db.ExtractosCliente
                .Where(e => e.Empresa == empresa && e.Número == cliente && e.TipoApunte == "1" && e.Fecha >= fechaDesde && e.Fecha < fechaHasta && e.Estado != Constantes.ExtractosCliente.Estados.DEUDA_VENCIDA)
                .Select(extractoEncontrado => new ExtractoClienteDTO
                {
                    id = extractoEncontrado.Nº_Orden,
                    fecha = extractoEncontrado.Fecha,
                    importe = extractoEncontrado.Importe
                })
                .OrderBy(e => e.fecha)
                .ToListAsync();

            // Crear DTO con datos del modelo 347
            Mod347DTO modelo = new Mod347DTO
            {
                nombre = clienteDb.Nombre?.Trim() ?? "",
                cifNif = clienteDb.CIF_NIF?.Trim() ?? "",
                direccion = clienteDb.Dirección?.Trim() ?? "",
                codigoPostal = clienteDb.CodPostal?.Trim() ?? "",
                trimestre = new decimal[4]
            };

            for (int i = 0; i <= 3; i++)
            {
                modelo.trimestre[i] = extracto.Where(e => (e.fecha.Month + 2) / 3 == i + 1).Sum(e => e.importe);
            }

            // Cargar datos de la empresa declarante
            Empresa empresaDeclarante = await db.Empresas.SingleOrDefaultAsync(e => e.Número == empresa);
            if (empresaDeclarante == null)
            {
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, "Empresa no encontrada");
            }

            // Generar PDF
            GeneradorPdfModelo347 generador = new GeneradorPdfModelo347();
            byte[] pdfBytes = generador.Generar(modelo, empresaDeclarante, ejercicio);

            // Devolver PDF
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(pdfBytes)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = $"Modelo347_{cliente}_{ejercicio}.pdf"
            };

            return response;
        }

        // GET: api/ExtractosCliente/5
        [ResponseType(typeof(ExtractoCliente))]
        public async Task<IHttpActionResult> GetExtractoCliente(int id)
        {
            ExtractoCliente extractoCliente = await db.ExtractosCliente.FindAsync(id);
            return extractoCliente == null ? NotFound() : (IHttpActionResult)Ok(extractoCliente);
        }

        // GET: api/ExtractosCliente?empresa=1&asiento=123
        [ResponseType(typeof(List<ExtractoClienteDTO>))]
        public async Task<IHttpActionResult> GetExtractoCliente(string empresa, int asiento)
        {
            List<ExtractoClienteDTO> extractoCliente = await db.ExtractosCliente
                .Where(e => e.Empresa == empresa && e.Asiento == asiento)
                .Select(e => new ExtractoClienteDTO
                {
                    id = e.Nº_Orden,
                    empresa = e.Empresa.Trim(),
                    asiento = e.Asiento,
                    cliente = e.Número.Trim(),
                    nombre = e.Cliente != null ? e.Cliente.Nombre.Trim() : e.Número.Trim(),
                    contacto = e.Contacto.Trim(),
                    fecha = e.Fecha,
                    tipo = e.TipoApunte.Trim(),
                    documento = e.Nº_Documento.Trim(),
                    efecto = e.Efecto.Trim(),
                    concepto = e.Concepto.Trim(),
                    importe = e.Importe,
                    importePendiente = e.ImportePdte,
                    vendedor = e.Vendedor.Trim(),
                    vencimiento = e.FechaVto ?? e.Fecha,
                    ccc = e.CCC.Trim(),
                    ruta = e.Ruta.Trim(),
                    estado = e.Estado.Trim(),
                    formaPago = e.FormaPago.Trim()
                })
                .ToListAsync();

            if (extractoCliente == null || !extractoCliente.Any())
            {
                return Ok(new List<ExtractoClienteDTO>());
            }

            return Ok(extractoCliente);
        }

        [HttpGet]
        [Route("api/ExtractosCliente/EnviarCorreoDomiciliacionDia")]
        // GET: api/Clientes/5
        [ResponseType(typeof(List<EfectoDomiciliado>))]
        public IHttpActionResult EnviarCorreoDomiciliacionDia()
        {
            IServicioDomiciliaciones servicio = new ServicioDomiciliaciones();
            GestorDomiciliaciones gestor = new GestorDomiciliaciones(servicio, _servicioCorreo);
            DateTime fechaMovimientos = DateTime.Today;

            IEnumerable<EfectoDomiciliado> respuesta = gestor.EnviarCorreoDomiciliacion(fechaMovimientos);

            return Ok(respuesta.ToList());
        }


        // PUT: api/ExtractosCliente/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutExtractoCliente(ExtractoClienteDTO extractoCliente)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (extractoCliente == null)
            {
                return BadRequest();
            }

            ExtractoCliente extractoActual = db.ExtractosCliente.Single(e => e.Nº_Orden == extractoCliente.id);
            bool enviarCorreoDelCambio = false;
            MailMessage correo = new MailMessage();
            bool hayCambios = false;

            if (extractoActual.FechaVto != extractoCliente.vencimiento)
            {
                hayCambios = true;
                extractoActual.FechaVto = extractoCliente.vencimiento;
            }

            if (extractoActual.CCC != extractoCliente.ccc)
            {
                hayCambios = true;
                extractoActual.CCC = string.IsNullOrWhiteSpace(extractoCliente.ccc) ? null : extractoCliente.ccc;
            }

            if (extractoActual.Estado != extractoCliente.estado)
            {
                hayCambios = true;
                if (extractoActual.Estado?.ToUpper() == Constantes.ExtractosCliente.Estados.RETENIDO || extractoCliente.estado?.ToUpper() == Constantes.ExtractosCliente.Estados.RETENIDO)
                {
                    enviarCorreoDelCambio = true;
                }

                try
                {
                    correo.From = new MailAddress(Constantes.Correos.CORREO_ADMON, "NUEVA VISIÓN (Administración)");
                    IQueryable<SeguimientoCliente> seguimientos = db.SeguimientosClientes.Where(s => s.NumOrdenExtracto == extractoActual.Nº_Orden);
                    IQueryable<ParametroUsuario> correosPara = db.ParametrosUsuario.Where(p => p.Empresa == extractoActual.Empresa && seguimientos.Where(s => s.Usuario != extractoCliente.usuario).Select(s => s.Usuario.Substring(s.Usuario.IndexOf("\\") + 1).Trim()).Contains(p.Usuario) && p.Clave == "CorreoDefecto");
                    foreach (string correoPara in correosPara.Select(c => c.Valor).Distinct())
                    {
                        correo.To.Add(correoPara);
                    }
                    correo.Subject = $"Cambio de estado retenido en extracto cliente (cliente {extractoActual.Número.Trim()})";
                    correo.Bcc.Add("carlosadrian@nuevavision.es");
                    correo.IsBodyHtml = true;
                    correo.Body = GenerarCorreoCambioEstado(seguimientos, extractoActual, extractoCliente);
                }
                catch
                {
                    correo.To.Add(new MailAddress(Constantes.Correos.CORREO_ADMON));
                    correo.Subject = $"[ERROR {extractoActual.Número.Trim()}] Cambio de estado retenido en extracto cliente";
                }

                extractoActual.Estado = string.IsNullOrWhiteSpace(extractoCliente.estado) ? null : extractoCliente.estado.ToUpper();
            }

            if (extractoActual.FormaPago != extractoCliente.formaPago)
            {
                hayCambios = true;
                extractoActual.FormaPago = extractoCliente.formaPago.ToUpper();
            }

            if (!hayCambios)
            {
                throw new Exception("No hay ningún cambio que guardar");
            }
            //db.Entry(extractoCliente).State = EntityState.Modified;

            try
            {
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
                if (enviarCorreoDelCambio)
                {
                    _ = _servicioCorreo.EnviarCorreoSMTP(correo);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ExtractoClienteExists(extractoCliente.id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Se ha producido un error al actualizar el extracto del producto", ex);
            }

            return StatusCode(HttpStatusCode.NoContent);
        }

        private string GenerarCorreoCambioEstado(IQueryable<SeguimientoCliente> seguimientos, ExtractoCliente extractoAnterior, ExtractoClienteDTO extractoNuevo)
        {
            StringBuilder s = new StringBuilder();

            _ = s.AppendLine("<!DOCTYPE html>");
            _ = s.AppendLine("<html lang='es'>");
            _ = s.AppendLine("<head>");
            _ = s.AppendLine("<meta http-equiv='Content-Type' content='text/html; charset=utf-8' />");
            _ = s.AppendLine("<meta name='viewport' content='width=device-width'>");
            _ = s.AppendLine("<title>Cambio de estado</title>");
            _ = s.AppendLine("<style></style>");
            _ = s.AppendLine("</head>");

            _ = s.AppendLine("<body>");

            //<!-- Header --> 
            _ = s.AppendLine("<table role='presentation' border='0' cellspacing='0' width='100%'>");
            _ = s.AppendLine("<tr>");
            _ = s.AppendLine("<td width=\"50%\" align='left'><img src=\"http://www.productosdeesteticaypeluqueriaprofesional.com/logofra.jpg\"></td>");
            _ = s.AppendLine("</tr>");
            _ = s.AppendLine("</table>");

            //<!-- Body --> 
            _ = s.AppendLine("<p>Se ha modificado un estado retenido.</p>");
            _ = s.AppendLine("<table role='presentation' border='0' cellspacing='0' width='100%'>");
            _ = s.AppendLine("<tr>");
            _ = s.AppendLine("<td align='left' style='border: 1px solid black;color:#333;padding:3px'>Datos de los efectos</td>");
            _ = s.AppendLine("</tr>");
            foreach (SeguimientoCliente seg in seguimientos.OrderBy(e => e.NºOrden))
            {
                _ = s.AppendLine("<tr>");
                _ = s.AppendLine("<td style='border: 1px solid black;color:#333;padding:3px'>");
                _ = s.AppendLine("<ul>");
                _ = s.AppendLine("<li>Fecha: " + seg.Fecha_Modificación.ToString() + "</li>");
                _ = s.AppendLine($"<li>{seg.Comentarios}</li>");
                _ = s.AppendLine("</ul>");
                _ = s.AppendLine("</td>");
                _ = s.AppendLine("</tr>");
            }
            _ = s.AppendLine("</table>");


            //<!-- Footer -->
            _ = s.AppendLine("<table role='presentation' border='0' cellspacing='0' width='100%'>");
            _ = s.AppendLine("</table>");
            //s.AppendLine("</div>");
            _ = s.AppendLine("</body>");


            return s.ToString();
        }

        /*

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
   */

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool ExtractoClienteExists(int id)
        {
            return db.ExtractosCliente.Any(e => e.Nº_Orden == id);
        }
    }
}