using Microsoft.Reporting.WebForms;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
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

        internal FacturasController(IServicioFacturas servicio, IGestorFacturas gestor)
        {
            this.servicio = servicio;
            this.gestor = gestor;
        }

        internal const string MOTIVO_SOLO_EMPLEADOS_CORREO = "Solo los empleados pueden enviar facturas por correo.";
        // (TNV 07/10/26: y el cliente de la tienda, solo las suyas; ver ComprobarEnvioDelCliente)

        /// <summary>
        /// Nesto#259: mandar facturas a una dirección cualquiera (y ver el correo de facturas de un cliente) es solo para
        /// empleados (token de Nesto con IsEmployee=true). Una clienta de TNV o un vendedor de NestoApp no puede.
        /// </summary>
        private bool EsEmpleado()
            => (User?.Identity as System.Security.Claims.ClaimsIdentity)?.FindFirst("IsEmployee")?.Value == "true";

        // GET api/Facturas
        // NestoAPI#601: exige token y un cliente de la tienda solo ve las suyas (antes era anónimo)
        [HttpGet]
        [Authorize]
        [Route("api/Facturas/FacturaJson")]
        [ResponseType(typeof(Factura))]
        public async Task<IHttpActionResult> GetFacturaJson(string empresa, string numeroFactura)
        {
            string clienteTienda = ClienteDelToken();
            if (clienteTienda != null && !FacturaEsDelCliente(clienteTienda, empresa, numeroFactura))
            {
                return Content(HttpStatusCode.Forbidden, MOTIVO_SOLO_FACTURAS_PROPIAS_VER);
            }

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
        // NestoAPI#601: exige token y un cliente de la tienda solo descarga las suyas (antes cualquiera con la URL
        // descargaba cualquier factura cambiando el número, que es correlativo)
        [HttpGet]
        [Authorize]
        public async Task<HttpResponseMessage> GetFactura(string empresa, string numeroFactura, bool papelConMembrete = false, bool mostrarImagenes = false)
        {
            string clienteTienda = ClienteDelToken();
            if (clienteTienda != null && !FacturaEsDelCliente(clienteTienda, empresa, numeroFactura))
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(MOTIVO_SOLO_FACTURAS_PROPIAS_VER)
                };
            }

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

                // TiendasNuevaVision#15: los clientes de la tienda online (token con claim "cliente")
                // usan siempre QuestPDF; no tienen ParametrosUsuario y no queremos RDLC para ellos
                bool esClienteTienda = (User as System.Security.Claims.ClaimsPrincipal)?.HasClaim(c => c.Type == "cliente") == true;

                var result = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = gestor.FacturasEnPDF(facturas, papelConMembrete, User.Identity.Name, mostrarImagenes, esClienteTienda)
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
            catch (NestoAPI.Infraestructure.Exceptions.NestoBusinessException ex) when ((int)ex.StatusCode < 500)
            {
                // NestoAPI#573: pedir una factura que no existe es un aviso, no un fallo: sin ficha en ELMAH
                return new HttpResponseMessage(ex.StatusCode)
                {
                    Content = new StringContent(ex.Message),
                    ReasonPhrase = "Factura no encontrada"
                };
            }
            catch (Exception ex)
            {
                // NestoAPI#453: sin esto el fallo no dejaba rastro en ningún sitio. El PDF del
                // pedido 925368 llevaba dos días fallando ("No cuadran los vencimientos con el
                // total de la factura") y en ELMAH no había ni una línea, porque el error se
                // convertía en un BadRequest y ahí se acababa. Un error que no se registra
                // cuesta el doble: hay que reproducirlo para poder verlo.
                ElmahHelper.Log(new Exception(
                    $"[Facturas] No se ha podido generar el PDF de {numeroFactura} " +
                    $"(empresa {empresa}): {ex.Message}", ex));

                var errorResponse = new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(ex.Message),
                    ReasonPhrase = "Error al generar la factura"
                };
                return errorResponse;
            }
        }

        // Sin [Authorize] a propósito (NestoAPI#601): no tienen llamante en Nesto, NestoApp ni TNV ni job de Hangfire;
        // si se lanzan solas será desde fuera del repo (Task Scheduler de RDS2016) y sin token. Confirmarlo antes de
        // protegerlas (#402 / #190).
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

        // POST api/Facturas/EnviarPorCorreo   { "Empresa": "1", "Facturas": ["NV2616199"], "Correos": ["cliente@correo.es"] }
        /// <summary>
        /// Nesto#259 (Manuel, 05/10/26): las facturas marcadas en la ficha comercial del cliente, en un solo correo, a
        /// los correos que escribe el usuario («;» o «,» para varios). Todas del mismo cliente y como mucho
        /// <see cref="GestorFacturas.MAXIMO_FACTURAS_POR_CORREO"/>. 400 con el motivo si lo pedido no vale; 502 si el
        /// servidor de correo no lo acepta (con el mensaje para el usuario).
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Facturas/EnviarPorCorreo")]
        [ResponseType(typeof(ResultadoEnvioFacturasCorreoDTO))]
        public async Task<IHttpActionResult> EnviarPorCorreo([FromBody] EnvioFacturasCorreoDTO envio)
        {
            // TNV (07/10/26): el cliente de la tienda (token con claim "cliente") también se puede mandar sus facturas a
            // donde quiera (su gestoría), pero SOLO las suyas. Empleados, como hasta ahora; vendedores y demás, no.
            string clienteTienda = ClienteDelToken();
            if (clienteTienda == null && !EsEmpleado())
            {
                return Content(HttpStatusCode.Forbidden, MOTIVO_SOLO_EMPLEADOS_CORREO);
            }
            if (envio == null)
            {
                return BadRequest("Faltan las facturas y el correo.");
            }
            if (clienteTienda != null)
            {
                IHttpActionResult rechazo = ComprobarEnvioDelCliente(clienteTienda, envio);
                if (rechazo != null)
                {
                    return rechazo;
                }
            }
            try
            {
                ResultadoEnvioFacturasCorreoDTO resultado = await gestor.EnviarFacturasACorreo(envio.Empresa, envio.Facturas,
                    envio.Correos, User?.Identity?.Name).ConfigureAwait(false);
                return resultado.Enviado ? (IHttpActionResult)Ok(resultado) : Content(HttpStatusCode.BadGateway, resultado);
            }
            catch (NestoAPI.Infraestructure.Exceptions.NestoBusinessException ex) when ((int)ex.StatusCode < 500)
            {
                return BadRequest(ex.Message);
            }
        }

        internal const string MOTIVO_SOLO_FACTURAS_PROPIAS = "Solo puedes enviar por correo tus propias facturas.";
        internal const string MOTIVO_SOLO_FACTURAS_PROPIAS_VER = "Solo puedes ver tus propias facturas.";
        internal const string MOTIVO_DEMASIADOS_ENVIOS = "Has enviado muchas facturas por correo en poco rato. Vuelve a intentarlo más tarde.";

        /// <summary>TNV: el número de cliente del token de la tienda (claim "cliente"), o null si no es un cliente.</summary>
        private string ClienteDelToken()
        {
            string cliente = (User?.Identity as System.Security.Claims.ClaimsIdentity)?.FindFirst("cliente")?.Value?.Trim();
            return string.IsNullOrEmpty(cliente) ? null : cliente;
        }

        /// <summary>
        /// TNV (07/10/26): un cliente solo manda facturas suyas. Si alguna no es suya (o no existe) → 403 con el mismo
        /// texto, sin decir cuál, para no desvelar qué facturas existen. Además, un tope de envíos por hora y cliente
        /// (<see cref="LimitadorEnviosFacturasCliente"/>). Null si se puede seguir. Lo demás (máximo de facturas,
        /// correos bien escritos, mismo cliente…) lo sigue validando <see cref="IGestorFacturas.EnviarFacturasACorreo"/>.
        /// </summary>
        private IHttpActionResult ComprobarEnvioDelCliente(string cliente, EnvioFacturasCorreoDTO envio)
        {
            List<string> numeros = (envio.Facturas ?? Enumerable.Empty<string>())
                .Select(f => f?.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (numeros.Count > GestorFacturas.MAXIMO_FACTURAS_POR_CORREO)
            {
                // El gestor lo rechaza (400) antes de leer nada: no hace falta mirar de quién son
                return null;
            }
            foreach (string numero in numeros)
            {
                if (!FacturaEsDelCliente(cliente, envio.Empresa, numero))
                {
                    return Content(HttpStatusCode.Forbidden, MOTIVO_SOLO_FACTURAS_PROPIAS);
                }
            }
            if (numeros.Any() && !LimitadorEnviosFacturasCliente.Permitir(cliente))
            {
                return Content((HttpStatusCode)429, MOTIVO_DEMASIADOS_ENVIOS);
            }
            return null;
        }

        /// <summary>
        /// TNV / NestoAPI#601: si la factura es de ese cliente (CabFacturaVta.Nº_Cliente). Inexistente = no es suya, para
        /// no desvelar qué facturas existen.
        /// </summary>
        private bool FacturaEsDelCliente(string cliente, string empresa, string numeroFactura)
        {
            if (string.IsNullOrWhiteSpace(numeroFactura))
            {
                return false;
            }
            string empresaFactura = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            CabFacturaVta cab = servicio.CargarCabFactura(empresaFactura, numeroFactura.Trim());
            return cab != null && string.Equals(cab.Nº_Cliente?.Trim(), cliente, StringComparison.OrdinalIgnoreCase);
        }

        // GET api/Facturas/CorreoFacturas?empresa=1&numeroFactura=NV2616199
        /// <summary>Nesto#259: el correo de facturas del cliente de esa factura, para proponerlo al mandarla. Vacío si no tiene.</summary>
        [HttpGet]
        [Authorize]
        [Route("api/Facturas/CorreoFacturas")]
        [ResponseType(typeof(CorreoFacturasDTO))]
        public IHttpActionResult GetCorreoFacturas(string empresa, string numeroFactura)
        {
            if (!EsEmpleado())
            {
                return Content(HttpStatusCode.Forbidden, MOTIVO_SOLO_EMPLEADOS_CORREO);
            }
            if (string.IsNullOrWhiteSpace(numeroFactura))
            {
                return BadRequest("Falta la factura.");
            }
            string correo = servicio.LeerCorreoFacturas(string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim(),
                numeroFactura.Trim());
            return Ok(new CorreoFacturasDTO { Correo = correo?.Trim() ?? string.Empty });
        }

        // Sin [Authorize] a propósito (NestoAPI#601): no tienen llamante en Nesto, NestoApp ni TNV ni job de Hangfire;
        // si se lanzan solas será desde fuera del repo (Task Scheduler de RDS2016) y sin token. Confirmarlo antes de
        // protegerlas (#402 / #190).
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


        // NestoAPI#171: [Authorize] para que ELMAH registre el usuario del JWT en caso
        // de auto-fix de descuadre. Sin este atributo, UserSyncHandler no propaga el
        // principal a HttpContext.Current.User y los errores quedan anónimos.
        [Authorize]
        [HttpPost]
        [Route("api/Facturas/CrearFactura")]
        public async Task<IHttpActionResult> CrearFactura([FromBody] dynamic parametros)
        {
            string empresa = parametros.Empresa;
            int pedido = parametros.Pedido;
            string usuario = parametros.Usuario;
            // Capturado en el controller (donde el principal del JWT es de fiar) para
            // poder usarlo como identidad autenticada en los logs de ELMAH y evitar
            // que el `usuario` del cuerpo —que el cliente puede falsear— se use como
            // si fuera la identidad real.
            string usuarioAutenticado = User.Identity?.Name;
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
            var resultado = await gestor.CrearFactura(empresa, pedido, usuario, usuarioAutenticado);
            return Ok(resultado);
        }
    }
}