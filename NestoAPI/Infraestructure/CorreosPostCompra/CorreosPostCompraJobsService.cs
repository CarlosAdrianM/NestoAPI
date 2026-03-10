using NestoAPI.Infraestructure.OpenAI;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Mail;
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

                var servicio = new ServicioRecomendacionesPostCompra();
                var correos = await servicio
                    .ObtenerCorreosSemana(Constantes.Empresas.EMPRESA_POR_DEFECTO, fechaDesde, fechaHasta)
                    .ConfigureAwait(false);

                if (!correos.Any())
                {
                    return;
                }

                // Modo test: redirigir todos los correos a los emails de prueba
                bool modoTest = ConfigurationManager.AppSettings["CorreosPostCompra:ModoTest"]?.ToLower() == "true";
                if (modoTest)
                {
                    string emailsTestConfig = ConfigurationManager.AppSettings["CorreosPostCompra:EmailsTest"] ?? "";
                    correos = AplicarModoTest(correos, emailsTestConfig);
                }

                // Programar envíos para el sábado a las 10:00 (3 días después del miércoles)
                DateTime fechaEnvio = hoy.AddDays(3).Date.AddHours(10);

                foreach (var correo in correos)
                {
                    try
                    {
                        Hangfire.BackgroundJob.Schedule(
                            () => EnviarCorreoPostCompra(correo),
                            fechaEnvio);
                    }
                    catch (Exception)
                    {
                        // Continuar con los demás correos
                    }
                }
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
                string htmlCorreo = await generador
                    .GenerarContenidoHtml(datos)
                    .ConfigureAwait(false);

                if (string.IsNullOrEmpty(htmlCorreo))
                {
                    return;
                }

                string asunto = $"Saca el máximo partido a tu compra, {datos.ClienteNombre}";
                string remitente = ConfigurationManager.AppSettings["CorreosPostCompra:Remitente"]
                    ?? "nuevavision@nuevavision.es";

                string htmlCompleto = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{asunto}</title>
</head>
<body style=""margin: 0; padding: 0; font-family: Arial, sans-serif;"">
    {htmlCorreo}
</body>
</html>";

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress(remitente, "El equipo de Nueva Visión");
                    mail.To.Add(datos.ClienteEmail);
                    mail.Subject = asunto;
                    mail.Body = htmlCompleto;
                    mail.IsBodyHtml = true;

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
    }
}
