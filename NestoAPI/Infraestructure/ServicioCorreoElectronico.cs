using Microsoft.Extensions.Logging;
using System;
using System.Configuration;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public class ServicioCorreoElectronico : IServicioCorreoElectronico
    {
        private readonly ILogger<ServicioCorreoElectronico> _logger;

        public ServicioCorreoElectronico(ILogger<ServicioCorreoElectronico> logger = null)
        {
            _logger = logger;
        }
        public bool EnviarCorreoSMTP(MailMessage mail)
        {
            using (SmtpClient client = new SmtpClient())
            {
                client.Port = 587;
                client.EnableSsl = true;
                client.DeliveryMethod = SmtpDeliveryMethod.Network;
                client.UseDefaultCredentials = false;
                string contrasenna = ConfigurationManager.AppSettings["office365password"];
                client.Credentials = new System.Net.NetworkCredential("nesto@nuevavision.es", contrasenna);
                client.Host = "smtp.office365.com";

                // Carlos 23/10/25: A veces no conecta a la primera, reintentamos después de 2s
                try
                {
                    client.Send(mail);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error al enviar el correo electrónico. Reintentando...");
                    try
                    {
                        Task.Delay(2000).Wait(); // Esperar 2 segundos
                        client.Send(mail);
                        return true;
                    }
                    catch (Exception ex2)
                    {
                        _logger?.LogError(ex2, "Error al reintentar enviar el correo electrónico.");
                        return false;
                    }
                }
            }
        }
    }
}