using Elmah;
using NestoAPI.Infraestructure.OpenAI;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>
    /// Servicio con métodos estáticos para jobs de Hangfire de correos post-compra.
    /// Issue #74: Sistema de correos automáticos con videos personalizados post-compra.
    /// Se ejecuta los miércoles a las 20:30, procesa albaranes de la semana y programa
    /// envíos individuales para el sábado a las 10:00.
    /// </summary>
    public class CorreosPostCompraJobsService
    {
        /// <summary>
        /// Job semanal (miércoles 20:30). Obtiene los albaranes de la semana,
        /// agrupa por cliente y programa los envíos para 3 días después (sábado 10:00).
        /// </summary>
        public static async Task ProcesarCorreosSemanales()
        {
            try
            {
                DateTime hoy = DateTime.Today;
                DateTime fechaDesde = hoy.AddDays(-6);
                DateTime fechaHasta = hoy;

                ErrorLog.GetDefault(null)?.Log(new Error(
                    new Exception($"[CorreosPostCompra] Iniciando ProcesarCorreosSemanales. Fechas: {fechaDesde:dd/MM/yyyy} - {fechaHasta:dd/MM/yyyy}")));

                var servicio = new ServicioRecomendacionesPostCompra();
                var correos = await servicio
                    .ObtenerCorreosSemana(Constantes.Empresas.EMPRESA_POR_DEFECTO, fechaDesde, fechaHasta)
                    .ConfigureAwait(false);

                if (!correos.Any())
                {
                    ErrorLog.GetDefault(null)?.Log(new Error(
                        new Exception("[CorreosPostCompra] ObtenerCorreosSemana devolvió 0 correos. No se programa nada.")));
                    return;
                }

                ErrorLog.GetDefault(null)?.Log(new Error(
                    new Exception($"[CorreosPostCompra] ObtenerCorreosSemana devolvió {correos.Count} correos.")));

                // Modo test: redirigir todos los correos a los emails de prueba
                bool modoTest = ConfigurationManager.AppSettings["CorreosPostCompra:ModoTest"]?.ToLower() == "true";
                if (modoTest)
                {
                    string emailsTestConfig = ConfigurationManager.AppSettings["CorreosPostCompra:EmailsTest"] ?? "";
                    correos = AplicarModoTest(correos, emailsTestConfig);
                    ErrorLog.GetDefault(null)?.Log(new Error(
                        new Exception($"[CorreosPostCompra] Modo test activo. EmailsTest='{emailsTestConfig}'. Correos tras filtro: {correos.Count}")));
                }

                // Programar envíos para el sábado a las 10:00 (3 días después del miércoles)
                DateTimeOffset fechaEnvio = new DateTimeOffset(hoy.AddDays(3).AddHours(10), TimeZoneInfo.Local.GetUtcOffset(hoy));
                int programados = 0;

                foreach (var correo in correos)
                {
                    try
                    {
                        Hangfire.BackgroundJob.Schedule(
                            () => EnviarCorreoPostCompra(correo),
                            fechaEnvio);
                        programados++;
                    }
                    catch (Exception ex)
                    {
                        ErrorLog.GetDefault(null)?.Log(new Error(
                            new Exception($"[CorreosPostCompra] Error programando correo para {correo.ClienteEmail}: {ex.Message}", ex)));
                    }
                }

                ErrorLog.GetDefault(null)?.Log(new Error(
                    new Exception($"[CorreosPostCompra] Programados {programados} correos para {fechaEnvio:dd/MM/yyyy HH:mm}")));
            }
            catch (Exception)
            {
                throw; // Re-lanzar para que Hangfire lo registre y reintente
            }
        }

        /// <summary>
        /// Genera el contenido HTML con OpenAI y envía el correo por SMTP.
        /// </summary>
        public static async Task EnviarCorreoPostCompra(CorreoPostCompraClienteDTO datos)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(datos?.ClienteEmail))
                {
                    return;
                }

                if (datos.ProductosComprados == null || !datos.ProductosComprados.Any())
                {
                    return;
                }

                var generador = new GeneradorContenidoCorreoPostCompra(new ServicioOpenAI());
                var (asuntoGenerado, htmlCorreo) = await generador
                    .GenerarContenidoConAsunto(datos)
                    .ConfigureAwait(false);

                if (string.IsNullOrEmpty(htmlCorreo))
                {
                    return;
                }

                string asunto = !string.IsNullOrWhiteSpace(asuntoGenerado)
                    ? asuntoGenerado
                    : "Tus vídeos tutoriales de esta semana";
                string remitente = ConfigurationManager.AppSettings["CorreosPostCompra:Remitente"]
                    ?? "nuevavision@nuevavision.es";

                string pieApp = GenerarPieDescargaApp();

                string htmlCompleto = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{asunto}</title>
</head>
<body style=""margin: 0; padding: 0; font-family: Arial, sans-serif;"">
    {htmlCorreo}
    {pieApp}
</body>
</html>";

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress(remitente, "El equipo de Nueva Visión");
                    mail.To.Add(datos.ClienteEmail);
                    mail.Subject = asunto;
                    mail.Body = htmlCompleto;
                    mail.IsBodyHtml = true;
                    mail.Bcc.Add("carlosadrian@nuevavision.es");

                    // En modo test, añadir el resto de emails como CC
                    bool modoTest = ConfigurationManager.AppSettings["CorreosPostCompra:ModoTest"]?.ToLower() == "true";
                    if (modoTest)
                    {
                        string emailsTestConfig = ConfigurationManager.AppSettings["CorreosPostCompra:EmailsTest"] ?? "";
                        var emailsExtra = emailsTestConfig
                            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(e => e.Trim())
                            .Skip(1) // El primero ya es el destinatario principal
                            .Where(e => !string.IsNullOrWhiteSpace(e));
                        foreach (var emailCc in emailsExtra)
                        {
                            mail.CC.Add(emailCc);
                        }
                    }

                    var servicioCorreo = new ServicioCorreoElectronico();
                    servicioCorreo.EnviarCorreoSMTP(mail);
                }
            }
            catch (Exception)
            {
                throw; // Re-lanzar para que Hangfire lo registre y reintente
            }
        }

        /// <summary>
        /// En modo test, redirige todos los correos al primer email de la lista de test.
        /// Si no hay emails de test configurados, devuelve lista vacía (no se envía nada).
        /// </summary>
        internal static List<CorreoPostCompraClienteDTO> AplicarModoTest(
            List<CorreoPostCompraClienteDTO> correos, string emailsTestConfig)
        {
            if (string.IsNullOrWhiteSpace(emailsTestConfig))
            {
                return new List<CorreoPostCompraClienteDTO>();
            }

            var emailPrincipal = emailsTestConfig
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim())
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(emailPrincipal))
            {
                return new List<CorreoPostCompraClienteDTO>();
            }

            foreach (var correo in correos)
            {
                correo.ClienteEmail = emailPrincipal;
            }

            return correos;
        }

        /// <summary>
        /// Genera el pie del correo post-compra con enlace de descarga de la app
        /// y mención a los vídeos con protocolos disponibles.
        /// </summary>
        internal static string GenerarPieDescargaApp()
        {
            const string urlGooglePlay = "https://play.google.com/store/apps/details?id=com.nuevavision.nestotiendas";

            var s = new StringBuilder();

            s.AppendLine("<br/>");
            s.AppendLine("<hr style=\"border: none; border-top: 1px solid #ddd; margin: 20px 0;\" />");

            s.AppendLine("<table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" width=\"100%\" style=\"max-width: 600px;\">");
            s.AppendLine("<tr><td style=\"padding: 20px; background-color: #f8f9fa; border-radius: 8px;\">");

            s.AppendLine("<p style=\"font-size: 14px; color: #333; margin: 0 0 15px 0; font-family: Arial, sans-serif;\">");
            s.AppendLine("<strong>Todos los v&iacute;deos y protocolos en tu m&oacute;vil</strong>");
            s.AppendLine("</p>");

            s.AppendLine("<p style=\"font-size: 13px; color: #666; margin: 0 0 15px 0; font-family: Arial, sans-serif;\">");
            s.AppendLine("Descarga nuestra app y accede a todos los v&iacute;deos con sus protocolos paso a paso, ");
            s.AppendLine("fichas t&eacute;cnicas de productos y mucho m&aacute;s. &iexcl;Todo gratis!");
            s.AppendLine("</p>");

            s.AppendLine("<table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" width=\"100%\">");
            s.AppendLine("<tr>");

            s.AppendLine("<td style=\"vertical-align: middle;\">");
            s.AppendLine($"<a href=\"{urlGooglePlay}\" target=\"_blank\" style=\"text-decoration: none;\">");
            s.AppendLine("<img src=\"https://play.google.com/intl/en_us/badges/static/images/badges/es_badge_web_generic.png\" ");
            s.AppendLine("alt=\"Descargar en Google Play\" width=\"135\" style=\"width: 135px; height: auto; display: inline-block;\" />");
            s.AppendLine("</a>");
            s.AppendLine("</td>");

            s.AppendLine("<td style=\"vertical-align: middle; padding-left: 15px;\">");
            s.AppendLine("<p style=\"font-size: 13px; color: #333; margin: 0; font-family: Arial, sans-serif;\">");
            s.AppendLine("<strong>Descarga la app</strong> y accede a todos los contenidos.");
            s.AppendLine("</p>");
            s.AppendLine("</td>");

            s.AppendLine("</tr>");
            s.AppendLine("</table>");

            s.AppendLine($"<p style=\"font-size: 11px; color: #999; margin: 15px 0 0 0; font-family: Arial, sans-serif;\">");
            s.AppendLine($"&iquest;No ve la imagen? Descargue la app aqu&iacute;: <a href=\"{urlGooglePlay}\" style=\"color: #007bff;\">{urlGooglePlay}</a>");
            s.AppendLine("</p>");

            s.AppendLine("</td></tr>");
            s.AppendLine("</table>");

            return s.ToString();
        }
    }
}
