using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using System;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Controlador para servir paginas HTML de pago y redireccion Redsys.
    /// Issue #121: ok.html/ko.html (deep links para app movil)
    /// Issue Nesto#310: pagina de pago propia con resumen de efectos
    /// </summary>
    [RoutePrefix("pago")]
    [AllowAnonymous]
    public class PagoRedirectController : ApiController
    {
        private readonly IRedsysService _redsysService;
        private readonly NVEntities _dbInyectada;

        public PagoRedirectController(IRedsysService redsysService)
        {
            _redsysService = redsysService;
        }

        // Para tests: permite inyectar el contexto con DbSets falsos.
        public PagoRedirectController(IRedsysService redsysService, NVEntities db)
        {
            _redsysService = redsysService;
            _dbInyectada = db;
        }

        /// <summary>
        /// Pagina de pago con resumen de efectos y formulario hacia Redsys.
        /// URL: https://api.nuevavision.es/pago/{tokenAcceso}
        /// </summary>
        [HttpGet]
        [Route("{token:guid}")]
        public async Task<HttpResponseMessage> PaginaPago(Guid token)
        {
            NVEntities db = _dbInyectada ?? new NVEntities();
            try
            {
                var pago = await db.PagosTPV
                    .Include(p => p.PagosTPV_Efectos)
                    .FirstOrDefaultAsync(p => p.TokenAcceso == token)
                    .ConfigureAwait(false);

                if (pago == null)
                {
                    return GenerarPaginaError("Enlace de pago no encontrado",
                        "Este enlace no es valido o ha expirado.");
                }

                if (pago.Estado == Constantes.EstadosPagoTPV.AUTORIZADO)
                {
                    return GenerarPaginaError("Pago ya realizado",
                        "Este pago ya fue completado correctamente. No es necesario volver a pagar.");
                }

                if (pago.Estado == Constantes.EstadosPagoTPV.DENEGADO)
                {
                    var pagoRegenerado = await db.PagosTPV
                        .FirstOrDefaultAsync(p => p.PagoOriginalId == pago.Id)
                        .ConfigureAwait(false);

                    if (pagoRegenerado != null)
                    {
                        var redirectResponse = Request.CreateResponse(HttpStatusCode.Redirect);
                        redirectResponse.Headers.Location = new Uri($"/pago/{pagoRegenerado.TokenAcceso}", UriKind.Relative);
                        return redirectResponse;
                    }

                    return GenerarPaginaError("Pago denegado",
                        "Este pago fue denegado y no se ha podido generar un nuevo enlace. Por favor, contacte con administracion.");
                }

                // Generar parametros de Redsys para el formulario
                string urlBase = "https://api.nuevavision.es";
                string urlNotificacion = urlBase + "/api/Pagos/NotificacionRedsys";
                string urlOk = urlBase + "/pago/ok.html";
                string urlKo = urlBase + "/pago/ko.html";

                // Issue #165: si el pago tiene MetodoPago ("C" o "z"), el usuario ya lo
                // eligió en origen (típicamente desde NestoTiendas). Solo generamos el
                // formulario de ese método para no ofrecer cambio tras decisión explícita.
                // Si MetodoPago es null, se generan los dos formularios como antes.
                bool mostrarTarjeta = pago.MetodoPago == null || pago.MetodoPago == "C";
                bool mostrarBizum = pago.MetodoPago == null || pago.MetodoPago == "z";

                var parametrosTarjeta = mostrarTarjeta ? _redsysService.CrearParametrosTPVVirtual(
                    pago.Importe,
                    pago.Descripcion ?? $"Pago {pago.NumeroOrden}",
                    pago.Correo,
                    pago.Cliente?.Trim(),
                    urlNotificacion,
                    urlOk,
                    urlKo,
                    "C",
                    pago.NumeroOrden) : null;

                var parametrosBizum = mostrarBizum ? _redsysService.CrearParametrosTPVVirtual(
                    pago.Importe,
                    pago.Descripcion ?? $"Pago {pago.NumeroOrden}",
                    pago.Correo,
                    pago.Cliente?.Trim(),
                    urlNotificacion,
                    urlOk,
                    urlKo,
                    "z",
                    pago.NumeroOrden) : null;

                // NumeroOrden se mantiene estable desde la creacion del pago
                pago.FechaActualizacion = DateTime.Now;
                await db.SaveChangesAsync().ConfigureAwait(false);

                string nombreCliente = "";
                try
                {
                    var cliente = await db.Clientes
                        .Where(c => c.Empresa == pago.Empresa && c.Nº_Cliente == pago.Cliente)
                        .Select(c => new { c.Nombre })
                        .FirstOrDefaultAsync()
                        .ConfigureAwait(false);
                    nombreCliente = cliente?.Nombre?.Trim() ?? "";
                }
                catch { }

                // NestoAPI#197: si el enlace se generó sin correo, la página ofrece (sin obligar)
                // introducirlo para recibir el justificante. La checkbox de facturas electrónicas
                // solo se muestra si el cliente no tiene ya una persona de contacto con cargo 22.
                bool mostrarCapturaCorreo = string.IsNullOrWhiteSpace(pago.Correo);
                bool mostrarCheckboxFacturas = false;
                if (mostrarCapturaCorreo)
                {
                    try
                    {
                        mostrarCheckboxFacturas = !await db.PersonasContactoClientes
                            .AnyAsync(p => p.Empresa == pago.Empresa && p.NºCliente == pago.Cliente
                                && p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO)
                            .ConfigureAwait(false);
                    }
                    catch { }
                }

                string userAgent = Request.Headers.UserAgent?.ToString() ?? "";
                string urlPagina = Request.RequestUri?.ToString() ?? "";
                string html = GenerarHtmlPaginaPago(pago, nombreCliente, parametrosTarjeta, parametrosBizum, userAgent, urlPagina, mostrarCapturaCorreo, mostrarCheckboxFacturas);

                var response = Request.CreateResponse(HttpStatusCode.OK);
                response.Content = new StringContent(html, Encoding.UTF8, "text/html");
                return response;
            }
            finally
            {
                if (_dbInyectada == null)
                {
                    db.Dispose();
                }
            }
        }

        /// <summary>
        /// NestoAPI#197: guarda el correo que el cliente final introduce en la página de pago
        /// cuando el enlace se generó sin él, para poder enviarle el justificante (y el nuevo
        /// enlace si el pago se deniega). Si el correo no existía como persona de contacto del
        /// cliente, se da de alta: con cargo 22 (factura por correo electrónico) si lo pide y
        /// nadie recibe aún las facturas así; con cargo 14 (por defecto) en caso contrario.
        /// </summary>
        [HttpPost]
        [Route("{token:guid}/correo")]
        public async Task<IHttpActionResult> GuardarCorreo(Guid token, [FromBody] Models.Pagos.GuardarCorreoPagoDTO peticion)
        {
            string correo = peticion?.Correo?.Trim();
            if (!EsCorreoValido(correo))
            {
                return BadRequest("El correo electrónico no tiene un formato válido");
            }

            NVEntities db = _dbInyectada ?? new NVEntities();
            try
            {
                var pago = await db.PagosTPV
                    .FirstOrDefaultAsync(p => p.TokenAcceso == token)
                    .ConfigureAwait(false);

                if (pago == null)
                {
                    return NotFound();
                }
                if (pago.Estado == Constantes.EstadosPagoTPV.AUTORIZADO)
                {
                    return Conflict();
                }

                pago.Correo = correo;

                var personasCliente = await db.PersonasContactoClientes
                    .Where(p => p.Empresa == pago.Empresa && p.NºCliente == pago.Cliente)
                    .ToListAsync()
                    .ConfigureAwait(false);

                bool yaExiste = personasCliente.Any(p => p.CorreoElectrónico != null
                    && p.CorreoElectrónico.Trim().Equals(correo, StringComparison.OrdinalIgnoreCase));

                bool facturasElectronicas = false;
                if (!yaExiste)
                {
                    bool clienteTieneCargo22 = personasCliente
                        .Any(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO);
                    short cargo = !clienteTieneCargo22 && peticion.DeseaFacturasElectronicas
                        ? Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO
                        : Constantes.Clientes.CARGO_POR_DEFECTO;
                    facturasElectronicas = cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO;

                    // Misma numeración que GestorClientes: máximo + 1 dentro del cliente/contacto.
                    int ultimoNumero;
                    try
                    {
                        ultimoNumero = personasCliente
                            .Where(p => p.Contacto == pago.Contacto)
                            .Max(p => int.Parse(p.Número));
                    }
                    catch
                    {
                        ultimoNumero = 1;
                    }

                    db.PersonasContactoClientes.Add(new PersonaContactoCliente
                    {
                        Empresa = pago.Empresa,
                        NºCliente = pago.Cliente,
                        Contacto = pago.Contacto,
                        Número = (ultimoNumero + 1).ToString(),
                        Nombre = string.Empty,
                        Cargo = cargo,
                        CorreoElectrónico = correo,
                        // No asumimos consentimiento de marketing por querer el justificante.
                        EnviarBoletin = false,
                        Estado = Constantes.Clientes.PersonasContacto.ESTADO_POR_DEFECTO,
                        Usuario = USUARIO_NESTOPAGO
                    });
                }

                await db.SaveChangesAsync().ConfigureAwait(false);

                return Ok(new Models.Pagos.RespuestaGuardarCorreoPago
                {
                    CorreoGuardado = true,
                    FacturasElectronicas = facturasElectronicas
                });
            }
            finally
            {
                if (_dbInyectada == null)
                {
                    db.Dispose();
                }
            }
        }

        // El Usuario de los PersonaContactoCliente creados desde la página de pago, para poder
        // rastrear su origen.
        internal const string USUARIO_NESTOPAGO = "NestoPago";

        internal static bool EsCorreoValido(string correo)
        {
            return !string.IsNullOrWhiteSpace(correo)
                && System.Text.RegularExpressions.Regex.IsMatch(correo, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        }

        /// <summary>
        /// Pagina de redireccion para pagos exitosos.
        /// Accesible en: https://api.nuevavision.es/pago/ok.html
        /// </summary>
        [HttpGet]
        [Route("ok.html")]
        public HttpResponseMessage PagoOk()
        {
            string html = @"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta http-equiv=""refresh"" content=""0;url=nestotiendas://pago/ok"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Redirigiendo...</title>
    <style>
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
            background-color: #f5f5f5;
            text-align: center;
        }
        .message {
            padding: 20px;
        }
    </style>
    <script>
        window.location.href = 'nestotiendas://pago/ok';
    </script>
</head>
<body>
    <div class=""message"">
        <h2>Pago exitoso</h2>
        <p>Redirigiendo a la aplicacion...</p>
        <p><small>Si no se redirige automaticamente, <a href=""nestotiendas://pago/ok"">haga clic aqui</a></small></p>
    </div>
</body>
</html>";

            var response = Request.CreateResponse(HttpStatusCode.OK);
            response.Content = new StringContent(html, Encoding.UTF8, "text/html");
            return response;
        }

        /// <summary>
        /// Pagina de redireccion para pagos cancelados o fallidos.
        /// Accesible en: https://api.nuevavision.es/pago/ko.html
        /// </summary>
        [HttpGet]
        [Route("ko.html")]
        public HttpResponseMessage PagoKo()
        {
            string html = @"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta http-equiv=""refresh"" content=""0;url=nestotiendas://pago/ko"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Redirigiendo...</title>
    <style>
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
            background-color: #f5f5f5;
            text-align: center;
        }
        .message {
            padding: 20px;
        }
    </style>
    <script>
        window.location.href = 'nestotiendas://pago/ko';
    </script>
</head>
<body>
    <div class=""message"">
        <h2>Pago cancelado</h2>
        <p>Redirigiendo a la aplicacion...</p>
        <p><small>Si no se redirige automaticamente, <a href=""nestotiendas://pago/ko"">haga clic aqui</a></small></p>
    </div>
</body>
</html>";

            var response = Request.CreateResponse(HttpStatusCode.OK);
            response.Content = new StringContent(html, Encoding.UTF8, "text/html");
            return response;
        }

        private const string URL_LOGO = "https://www.productosdeesteticaypeluqueriaprofesional.com/img/cms/Landing/logo.png";

        private string GenerarHtmlPaginaPago(PagoTPV pago, string nombreCliente, Models.Pagos.ParametrosRedsysFirmados parametrosTarjeta, Models.Pagos.ParametrosRedsysFirmados parametrosBizum, string userAgent = "", string urlPagina = "", bool mostrarCapturaCorreo = false, bool mostrarCheckboxFacturas = false)
        {
            // Issue #162: iOS Safari deja la página en blanco al pulsar "Pagar con tarjeta" por
            // restricciones del ITP sobre cookies cross-site que necesita Redsys. Mientras
            // investigamos el fix definitivo, mostramos un aviso al usuario con el enlace
            // copiable para que pueda reintentar desde Safari si queda en blanco.
            bool esIOS = !string.IsNullOrEmpty(userAgent) &&
                (userAgent.IndexOf("iPhone", StringComparison.OrdinalIgnoreCase) >= 0
                 || userAgent.IndexOf("iPad", StringComparison.OrdinalIgnoreCase) >= 0
                 || userAgent.IndexOf("iPod", StringComparison.OrdinalIgnoreCase) >= 0);
            // Issue #165: un formulario u otro puede no existir si el MetodoPago del pago
            // fija la elección del usuario (p.ej. eligió Bizum en NestoTiendas).
            string formTarjeta = parametrosTarjeta == null ? "" : $@"
            <form action=""{parametrosTarjeta.UrlRedsys}"" method=""POST"">
                <input type=""hidden"" name=""Ds_SignatureVersion"" value=""{parametrosTarjeta.Ds_SignatureVersion}"" />
                <input type=""hidden"" name=""Ds_MerchantParameters"" value=""{parametrosTarjeta.Ds_MerchantParameters}"" />
                <input type=""hidden"" name=""Ds_Signature"" value=""{parametrosTarjeta.Ds_Signature}"" />
                <button type=""submit"" class=""btn-pagar btn-tarjeta"">&#128179; Pagar con tarjeta</button>
            </form>";

            string formBizum = parametrosBizum == null ? "" : $@"
            <form action=""{parametrosBizum.UrlRedsys}"" method=""POST"">
                <input type=""hidden"" name=""Ds_SignatureVersion"" value=""{parametrosBizum.Ds_SignatureVersion}"" />
                <input type=""hidden"" name=""Ds_MerchantParameters"" value=""{parametrosBizum.Ds_MerchantParameters}"" />
                <input type=""hidden"" name=""Ds_Signature"" value=""{parametrosBizum.Ds_Signature}"" />
                <button type=""submit"" class=""btn-pagar btn-bizum"">Pagar con Bizum</button>
            </form>";

            string filasEfectos = "";
            if (pago.PagosTPV_Efectos != null && pago.PagosTPV_Efectos.Any())
            {
                bool alternar = false;
                foreach (var efecto in pago.PagosTPV_Efectos)
                {
                    string bgColor = alternar ? "background-color: #faf5f7;" : "";
                    filasEfectos += $@"
                    <tr style=""{bgColor}"">
                        <td style=""padding: 12px 10px; border-bottom: 1px solid #f0e8ec;"">{HttpUtility.HtmlEncode(efecto.Documento?.Trim())}</td>
                        <td style=""padding: 12px 10px; border-bottom: 1px solid #f0e8ec; text-align: right; white-space: nowrap;"">{efecto.Importe:N2} &euro;</td>
                    </tr>";
                    alternar = !alternar;
                }
            }
            else
            {
                filasEfectos = $@"
                    <tr>
                        <td style=""padding: 12px 10px; border-bottom: 1px solid #f0e8ec;"">{HttpUtility.HtmlEncode(pago.Descripcion?.Trim() ?? "Pago")}</td>
                        <td style=""padding: 12px 10px; border-bottom: 1px solid #f0e8ec; text-align: right; white-space: nowrap;"">{pago.Importe:N2} &euro;</td>
                    </tr>";
            }

            string avisoIOS = esIOS ? $@"
            <div class=""aviso-ios"">
                <strong>&#9432; &iquest;Problemas al pagar desde iPhone?</strong>
                <p>Si tras pulsar el bot&oacute;n la p&aacute;gina queda en blanco, copia este enlace y &aacute;brelo en <b>Safari</b>:</p>
                <input type=""text"" readonly value=""{HttpUtility.HtmlAttributeEncode(urlPagina)}"" onclick=""this.select()"" />
            </div>" : "";

            // NestoAPI#197: captura opcional del correo cuando el enlace se generó sin él.
            // Bloque colapsado (details/summary, sin JS para el toggle) entre el total y los
            // botones de pago; guardar no interrumpe el flujo (el cliente puede pagar igual).
            string checkboxFacturas = mostrarCheckboxFacturas ? @"
                    <label class=""correo-check""><input type=""checkbox"" id=""chk-facturas"" checked /> Quiero recibir tambi&eacute;n las facturas en este correo <span>(en lugar de en papel)</span></label>" : "";

            string bloqueCorreo = !mostrarCapturaCorreo ? "" : $@"
            <details class=""correo-bloque"" id=""bloque-correo"">
                <summary>&#9993;&#65039; &iquest;Quieres el justificante por correo? <span class=""correo-opcional"">(opcional)</span></summary>
                <div class=""correo-form"">
                    <label for=""correo-pago"">Correo electr&oacute;nico</label>
                    <input type=""email"" id=""correo-pago"" inputmode=""email"" autocomplete=""email"" placeholder=""tucorreo@ejemplo.com"" />
                    {checkboxFacturas}
                    <button type=""button"" class=""btn-correo"" onclick=""guardarCorreoPago()"">Guardar</button>
                    <p class=""correo-aviso"">Tu correo se usar&aacute; solo para enviarte el justificante de este pago{(mostrarCheckboxFacturas ? " y, si lo marcas, tus facturas" : "")}.</p>
                    <p class=""correo-resultado"" id=""correo-resultado"" role=""status""></p>
                </div>
            </details>
            <script>
                function guardarCorreoPago() {{
                    var input = document.getElementById('correo-pago');
                    var resultado = document.getElementById('correo-resultado');
                    var correo = (input.value || '').trim();
                    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(correo)) {{
                        resultado.style.color = '#c0392b';
                        resultado.textContent = 'Introduce un correo válido.';
                        return;
                    }}
                    var chk = document.getElementById('chk-facturas');
                    var boton = document.querySelector('.btn-correo');
                    boton.disabled = true;
                    fetch('/pago/{pago.TokenAcceso}/correo', {{
                        method: 'POST',
                        headers: {{ 'Content-Type': 'application/json' }},
                        body: JSON.stringify({{ correo: correo, deseaFacturasElectronicas: chk ? chk.checked : false }})
                    }}).then(function (r) {{
                        boton.disabled = false;
                        if (r.ok) {{
                            resultado.style.color = '#27ae60';
                            resultado.textContent = '✓ Te enviaremos el justificante a ' + correo;
                        }} else {{
                            resultado.style.color = '#c0392b';
                            resultado.textContent = 'No se pudo guardar el correo. Puedes pagar igualmente.';
                        }}
                    }}).catch(function () {{
                        boton.disabled = false;
                        resultado.style.color = '#c0392b';
                        resultado.textContent = 'No se pudo guardar el correo. Puedes pagar igualmente.';
                    }});
                }}
            </script>";

            return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <link rel=""preconnect"" href=""https://sis.redsys.es"">
    <title>Pago - Nueva Visi&oacute;n</title>
    <style>
        * {{ box-sizing: border-box; margin: 0; padding: 0; }}
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background: linear-gradient(135deg, #f8f4f6 0%, #ede7eb 100%);
            color: #333;
            min-height: 100vh;
            display: flex;
            justify-content: center;
            align-items: flex-start;
            padding: 20px;
        }}
        .container {{
            background: white;
            border-radius: 16px;
            box-shadow: 0 4px 30px rgba(155, 142, 174, 0.2);
            max-width: 500px;
            width: 100%;
            overflow: hidden;
            margin-top: 20px;
        }}
        .header {{
            background: white;
            padding: 30px 25px 20px;
            text-align: center;
            border-bottom: 3px solid #9B8EAE;
        }}
        .header img {{
            max-height: 80px;
            margin-bottom: 10px;
        }}
        .header p {{
            color: #9B8EAE;
            font-size: 15px;
            font-weight: 500;
            letter-spacing: 0.5px;
        }}
        .content {{ padding: 25px; }}
        .info-row {{
            display: flex;
            justify-content: space-between;
            padding: 8px 0;
            font-size: 14px;
        }}
        .info-label {{ color: #9B8EAE; font-weight: 500; }}
        .divider {{ border: none; border-top: 1px solid #f0e8ec; margin: 15px 0; }}
        table {{ width: 100%; border-collapse: collapse; margin: 10px 0; }}
        th {{
            text-align: left;
            padding: 10px;
            color: #9B8EAE;
            font-size: 12px;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            border-bottom: 2px solid #f0e8ec;
        }}
        th:last-child {{ text-align: right; }}
        .total-row {{
            display: flex;
            justify-content: space-between;
            padding: 15px 0;
            font-size: 22px;
            font-weight: bold;
            color: #7B6E8E;
        }}
        .btn-pagar {{
            display: block;
            width: 100%;
            padding: 16px;
            color: white;
            border: none;
            border-radius: 10px;
            font-size: 18px;
            font-weight: bold;
            cursor: pointer;
            margin-top: 12px;
            transition: background 0.2s, transform 0.1s;
            letter-spacing: 0.3px;
        }}
        .btn-pagar:active {{ transform: scale(0.98); }}
        .btn-tarjeta {{
            background: linear-gradient(135deg, #9B8EAE 0%, #7B6E8E 100%);
        }}
        .btn-tarjeta:hover {{ background: linear-gradient(135deg, #8A7D9E 0%, #6B5E7E 100%); }}
        .btn-bizum {{
            background: linear-gradient(135deg, #05C3DD 0%, #0A9AB0 100%);
        }}
        .btn-bizum:hover {{ background: linear-gradient(135deg, #04B0C8 0%, #0889A0 100%); }}
        .footer {{
            text-align: center;
            padding: 18px;
            font-size: 12px;
            color: #b0a0b8;
            border-top: 1px solid #f0e8ec;
        }}
        .secure {{
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 6px;
            margin-top: 12px;
            font-size: 12px;
            color: #9B8EAE;
        }}
        .aviso-ios {{
            margin-top: 16px;
            padding: 12px 14px;
            background: #fff8e6;
            border: 1px solid #f0d890;
            border-radius: 8px;
            font-size: 13px;
            color: #6b5510;
        }}
        .aviso-ios strong {{ display: block; margin-bottom: 4px; }}
        .aviso-ios p {{ margin: 4px 0 8px 0; }}
        .aviso-ios input {{
            width: 100%;
            padding: 8px;
            border: 1px solid #e5d29a;
            border-radius: 6px;
            font-size: 12px;
            font-family: monospace;
            background: white;
        }}
        .correo-bloque {{
            margin: 12px 0 0 0;
            border: 1px solid #f0e8ec;
            border-radius: 10px;
            padding: 10px 14px;
            font-size: 14px;
        }}
        .correo-bloque summary {{
            cursor: pointer;
            color: #7B6E8E;
            font-weight: 500;
            outline: none;
        }}
        .correo-opcional {{ color: #b0a0b8; font-weight: normal; font-size: 12px; }}
        .correo-form {{ padding-top: 10px; }}
        .correo-form label {{ display: block; color: #9B8EAE; font-size: 13px; margin-bottom: 4px; }}
        .correo-form input[type=email] {{
            width: 100%;
            padding: 10px;
            border: 1px solid #d8ccd4;
            border-radius: 8px;
            font-size: 15px;
        }}
        .correo-check {{
            display: block;
            margin-top: 10px;
            font-size: 13px;
            color: #555;
        }}
        .correo-check span {{ color: #b0a0b8; }}
        .btn-correo {{
            margin-top: 10px;
            padding: 9px 18px;
            background: white;
            color: #7B6E8E;
            border: 1px solid #9B8EAE;
            border-radius: 8px;
            font-size: 14px;
            font-weight: 600;
            cursor: pointer;
        }}
        .btn-correo:disabled {{ opacity: 0.6; cursor: wait; }}
        .correo-aviso {{ margin-top: 8px; font-size: 11px; color: #b0a0b8; }}
        .correo-resultado {{ margin-top: 6px; font-size: 13px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <img src=""{URL_LOGO}"" alt=""Nueva Visi&oacute;n"" />
            <p>Resumen de pago</p>
        </div>
        <div class=""content"">
            <div class=""info-row"">
                <span class=""info-label"">Cliente</span>
                <span>{HttpUtility.HtmlEncode(nombreCliente)}</span>
            </div>
            <div class=""info-row"">
                <span class=""info-label"">Referencia</span>
                <span>{HttpUtility.HtmlEncode(pago.NumeroOrden)}</span>
            </div>
            <hr class=""divider"">
            <table>
                <tr>
                    <th>Concepto</th>
                    <th>Importe</th>
                </tr>
                {filasEfectos}
            </table>
            <hr class=""divider"">
            <div class=""total-row"">
                <span>Total</span>
                <span>{pago.Importe:N2} &euro;</span>
            </div>
            {bloqueCorreo}
            {formTarjeta}
            {formBizum}
            {avisoIOS}
            <div class=""secure"">
                &#128274; Pago seguro via Redsys
            </div>
        </div>
        <div class=""footer"">
            Nueva Visi&oacute;n &middot; www.nuevavision.es
        </div>
    </div>
</body>
</html>";
        }

        private HttpResponseMessage GenerarPaginaError(string titulo, string mensaje)
        {
            string html = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{HttpUtility.HtmlEncode(titulo)}</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
            background-color: #f5f5f5;
            text-align: center;
        }}
        .message {{
            background: white;
            padding: 40px;
            border-radius: 12px;
            box-shadow: 0 2px 20px rgba(0,0,0,0.1);
            max-width: 400px;
        }}
        h2 {{ color: #e74c3c; margin-bottom: 10px; }}
        p {{ color: #666; }}
    </style>
</head>
<body>
    <div class=""message"">
        <h2>{HttpUtility.HtmlEncode(titulo)}</h2>
        <p>{HttpUtility.HtmlEncode(mensaje)}</p>
    </div>
</body>
</html>";

            var response = Request.CreateResponse(HttpStatusCode.OK);
            response.Content = new StringContent(html, Encoding.UTF8, "text/html");
            return response;
        }
    }
}
