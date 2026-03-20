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

        public PagoRedirectController(IRedsysService redsysService)
        {
            _redsysService = redsysService;
        }

        /// <summary>
        /// Pagina de pago con resumen de efectos y formulario hacia Redsys.
        /// URL: https://api.nuevavision.es/pago/{tokenAcceso}
        /// </summary>
        [HttpGet]
        [Route("{token:guid}")]
        public async Task<HttpResponseMessage> PaginaPago(Guid token)
        {
            using (NVEntities db = new NVEntities())
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

                if (pago.Estado == "Autorizado")
                {
                    return GenerarPaginaError("Pago ya realizado",
                        "Este pago ya fue completado correctamente. No es necesario volver a pagar.");
                }

                // Generar parametros de Redsys para el formulario
                string urlBase = "https://api.nuevavision.es";
                string urlNotificacion = urlBase + "/api/Pagos/NotificacionRedsys";
                string urlOk = urlBase + "/pago/ok.html";
                string urlKo = urlBase + "/pago/ko.html";

                var parametrosTarjeta = _redsysService.CrearParametrosTPVVirtual(
                    pago.Importe,
                    pago.Descripcion ?? $"Pago {pago.NumeroOrden}",
                    pago.Correo,
                    pago.Cliente?.Trim(),
                    urlNotificacion,
                    urlOk,
                    urlKo,
                    "C");

                var parametrosBizum = _redsysService.CrearParametrosTPVVirtual(
                    pago.Importe,
                    pago.Descripcion ?? $"Pago {pago.NumeroOrden}",
                    pago.Correo,
                    pago.Cliente?.Trim(),
                    urlNotificacion,
                    urlOk,
                    urlKo,
                    "z");

                // Actualizar NumeroOrden con el de tarjeta (referencia principal)
                pago.NumeroOrden = parametrosTarjeta.NumeroOrden;
                pago.Estado = "Pendiente";
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

                string html = GenerarHtmlPaginaPago(pago, nombreCliente, parametrosTarjeta, parametrosBizum);

                var response = Request.CreateResponse(HttpStatusCode.OK);
                response.Content = new StringContent(html, Encoding.UTF8, "text/html");
                return response;
            }
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

        private string GenerarHtmlPaginaPago(PagoTPV pago, string nombreCliente, Models.Pagos.ParametrosRedsysFirmados parametrosTarjeta, Models.Pagos.ParametrosRedsysFirmados parametrosBizum)
        {
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

            return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
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
            <form action=""{parametrosTarjeta.UrlRedsys}"" method=""POST"">
                <input type=""hidden"" name=""Ds_SignatureVersion"" value=""{parametrosTarjeta.Ds_SignatureVersion}"" />
                <input type=""hidden"" name=""Ds_MerchantParameters"" value=""{parametrosTarjeta.Ds_MerchantParameters}"" />
                <input type=""hidden"" name=""Ds_Signature"" value=""{parametrosTarjeta.Ds_Signature}"" />
                <button type=""submit"" class=""btn-pagar btn-tarjeta"">&#128179; Pagar con tarjeta</button>
            </form>
            <form action=""{parametrosBizum.UrlRedsys}"" method=""POST"">
                <input type=""hidden"" name=""Ds_SignatureVersion"" value=""{parametrosBizum.Ds_SignatureVersion}"" />
                <input type=""hidden"" name=""Ds_MerchantParameters"" value=""{parametrosBizum.Ds_MerchantParameters}"" />
                <input type=""hidden"" name=""Ds_Signature"" value=""{parametrosBizum.Ds_Signature}"" />
                <button type=""submit"" class=""btn-pagar btn-bizum"">Pagar con Bizum</button>
            </form>
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
