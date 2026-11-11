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
        /// <summary>
        /// NestoAPI#854: un correo que el SMTP no acepta (ni al reintentar) queda en ELMAH con el motivo real (SendAsDenied,
        /// buzón lleno, credenciales…), de quién era, para quién y el asunto. Antes solo iba a un ILogger que es null y el
        /// motivo se perdía. ELMAH apunta además el usuario de la petición, si la hay. Nunca tumba el envío.
        /// </summary>
        private static void RegistrarFalloEnElmah(MailMessage mail, Exception primerIntento, Exception segundoIntento)
        {
            try
            {
                ElmahHelper.Log(new Exception(TextoFallo(mail, primerIntento, segundoIntento), segundoIntento));
            }
            catch
            {
                // Sin ELMAH (tests, arranque): el que llama ya sabe que no ha salido
            }
        }

        internal static string TextoFallo(MailMessage mail, Exception primerIntento, Exception segundoIntento)
        {
            string motivo = MensajeCompleto(segundoIntento);
            string primero = MensajeCompleto(primerIntento);
            return $"[NestoAPI#854] El servidor de correo no ha aceptado el correo «{mail?.Subject}» " +
                $"de {mail?.From?.Address} para {mail?.To} (CC: {mail?.CC}; CCO: {mail?.Bcc}; responder a: {mail?.ReplyToList}): {motivo}" +
                (primero == motivo ? string.Empty : $" (primer intento: {primero})");
        }

        private static string MensajeCompleto(Exception ex)
        {
            string texto = ex?.Message ?? string.Empty;
            for (Exception interna = ex?.InnerException; interna != null; interna = interna.InnerException)
            {
                texto += " → " + interna.Message;
            }
            return texto;
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
                        RegistrarFalloEnElmah(mail, ex, ex2);
                        return false;
                    }
                }
            }
        }
    }
}