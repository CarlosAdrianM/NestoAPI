using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Controlador para servir paginas HTML de redireccion despues de pagos con Redsys.
    /// Estas paginas redirigen automaticamente al deep link de la app movil.
    /// </summary>
    [RoutePrefix("pago")]
    [AllowAnonymous]
    public class PagoRedirectController : ApiController
    {
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
    }
}
