using Microsoft.Reporting.WebForms;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class FacturasController : ApiController
    {
        private readonly IServicioFacturas servicio;
        private readonly IGestorFacturas gestor;

        public FacturasController()
        {
            servicio = new ServicioFacturas();
            gestor = new GestorFacturas(servicio);
        }

        // GET api/Facturas
        [HttpGet]
        [Route("api/Facturas/FacturaJson")]
        [ResponseType(typeof(Factura))]
        public async Task<IHttpActionResult> GetFacturaJson(string empresa, string numeroFactura)
        {
            // CONTROL DE SEGURIDAD: Bloquear acceso a facturas de series que no permiten descarga
            // (series con CorreoDesdeFactura == null no se pueden descargar)
            if (numeroFactura.Length >= 2)
            {
                try
                {
                    string codigoSerie = numeroFactura.Substring(0, 2);
                    ISerieFactura serieFactura = GestorFacturas.LeerSerie(codigoSerie);

                    if (serieFactura.CorreoDesdeFactura == null)
                    {
                        string mensajeError = $"No está permitido acceder a facturas de la serie {codigoSerie}. Esta serie no permite descarga por motivos de seguridad.";
                        System.Diagnostics.Debug.WriteLine($"❌ SEGURIDAD: Intento de acceso a factura bloqueada: {numeroFactura}");
                        return Content(HttpStatusCode.Forbidden, mensajeError);
                    }
                }
                catch (Exception ex)
                {
                    // Si no se puede leer la serie, permitir continuar (puede ser una serie inexistente que dará error después)
                    System.Diagnostics.Debug.WriteLine($"⚠ Advertencia: No se pudo validar serie de factura {numeroFactura}: {ex.Message}");
                }
            }

            Factura factura = gestor.LeerFactura(empresa, numeroFactura);

            return Ok(factura);
        }

        // GET api/Facturas
        [HttpGet]
        public async Task<HttpResponseMessage> GetFactura(string empresa, string numeroFactura, bool papelConMembrete = false)
        {
            try
            {
                // CONTROL DE SEGURIDAD: Bloquear descarga de PDFs de series que no permiten descarga
                // (series con CorreoDesdeFactura == null no se pueden descargar)
                if (numeroFactura.Length >= 2)
                {
                    try
                    {
                        string codigoSerie = numeroFactura.Substring(0, 2);
                        ISerieFactura serieFactura = GestorFacturas.LeerSerie(codigoSerie);

                        if (serieFactura.CorreoDesdeFactura == null)
                        {
                            string mensajeError = $"No está permitido descargar PDFs de la serie {codigoSerie}. Esta serie no permite descarga por motivos de seguridad.";
                            System.Diagnostics.Debug.WriteLine($"❌ SEGURIDAD: Intento de descarga de PDF bloqueado: {numeroFactura}");
                            return new HttpResponseMessage(HttpStatusCode.Forbidden)
                            {
                                Content = new StringContent(mensajeError),
                                ReasonPhrase = "Serie no permitida"
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        // Si no se puede leer la serie, permitir continuar (puede ser una serie inexistente que dará error después)
                        System.Diagnostics.Debug.WriteLine($"⚠ Advertencia: No se pudo validar serie de factura {numeroFactura}: {ex.Message}");
                    }
                }

                FacturaLookup factura = new FacturaLookup { Empresa = empresa, Factura = numeroFactura };
                List<FacturaLookup> lista = new List<FacturaLookup>
                {
                    factura
                };
                List<Factura> facturas = gestor.LeerFacturas(lista);

                var result = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = gestor.FacturasEnPDF(facturas, papelConMembrete)
                };
                //result.Content.Headers.ContentDisposition =
                //    new ContentDispositionHeaderValue("attachment")
                //    {
                //        FileName = factura.Item2 + ".pdf"
                //    };
                result.Content.Headers.ContentType =
                    new MediaTypeHeaderValue("application/pdf");

                return result;
            }
            catch (Exception ex)
            {
                var errorResponse = new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(ex.Message),
                    ReasonPhrase = "Error al generar la factura"
                };
                return errorResponse;
            }
        }

        [HttpGet]
        [Route("api/Facturas/EnviarFacturasDia")]
        // GET: api/Clientes/5
        [ResponseType(typeof(List<FacturaCorreo>))]
        public async Task<IHttpActionResult> EnviarFacturasDia()
        {
            GestorFacturas gestor = new GestorFacturas();
            
            IEnumerable<FacturaCorreo> respuesta = await gestor.EnviarFacturasPorCorreo(DateTime.Today);

            return Ok(respuesta.ToList());
        }

        [HttpGet]
        [Route("api/Facturas/EnviarFacturasTrimestre")]
        // GET: api/Clientes/5
        [ResponseType(typeof(List<FacturaCorreo>))]
        public async Task<IHttpActionResult> EnviarFacturasTrimestre()
        {
            GestorFacturas gestor = new GestorFacturas();
            DateTime hoy = DateTime.Today;
            int quarterNumber = (hoy.Month - 1) / 3 + 1;
            int anno = hoy.Year;
            if (quarterNumber != 1)
            {
                quarterNumber--;
            } else
            {
                quarterNumber = 4;
                anno--;
            }
            DateTime firstDayOfQuarter = new DateTime(anno, (quarterNumber - 1) * 3 + 1, 1);
            DateTime lastDayOfQuarter = firstDayOfQuarter.AddMonths(3).AddDays(-1);

            List<ClienteCorreoFactura> respuesta = gestor.EnviarFacturasTrimestrePorCorreo(firstDayOfQuarter, lastDayOfQuarter);

            return Ok(respuesta.ToList());
        }


        [HttpPost]
        [Route("api/Facturas/CrearFactura")]
        public async Task<IHttpActionResult> CrearFactura([FromBody] dynamic parametros)
        {
            string empresa = parametros.Empresa;
            int pedido = parametros.Pedido;
            string usuario = parametros.Usuario;
            if (empresa == null)
            {
                return BadRequest("No se ha especificado la empresa");
            }
            if (pedido == 0)
            {
                return BadRequest("No se ha especificado el pedido");
            }

            // Las excepciones se propagan automáticamente al GlobalExceptionFilter
            // que las formatea con información rica de contexto
            var resultado = await gestor.CrearFactura(empresa, pedido, usuario);
            return Ok(resultado);
        }
    }
}