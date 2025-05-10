using Microsoft.Extensions.Logging;
using System;
using System.Configuration;
using System.Net.Mail;

namespace NestoAPI.Infraestructure
{
    public class ServicioCorreoElectronico : IServicioCorreoElectronico
    {
        private readonly ILogger<ServicioCorreoElectronico> _logger;

        public ServicioCorreoElectronico(ILogger<ServicioCorreoElectronico> logger)
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

                try
                {
                    client.Send(mail);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al enviar el correo electrónico.");
                    return false;
                }
            }
        }
    }
}