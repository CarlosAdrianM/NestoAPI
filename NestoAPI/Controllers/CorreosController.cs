using NestoAPI.Infraestructure;
using NestoAPI.Models.Correos;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    [Authorize]
    [RoutePrefix("api/Correos")]
    public class CorreosController : ApiController
    {
        private const string REMITENTE_DEFECTO = "nesto@nuevavision.es";
        private const string NOMBRE_REMITENTE_DEFECTO = "Nueva Visión";

        private readonly IServicioCorreoElectronico _servicio;

        public CorreosController() : this(new ServicioCorreoElectronico())
        {
        }

        public CorreosController(IServicioCorreoElectronico servicio)
        {
            _servicio = servicio;
        }

        // POST: api/Correos/Enviar
        [HttpPost]
        [Route("Enviar")]
        [ResponseType(typeof(EnvioCorreoRespuestaDTO))]
        public IHttpActionResult Enviar([FromBody] EnvioCorreoDTO dto)
        {
            if (dto == null)
            {
                return BadRequest("Falta el cuerpo de la petición.");
            }
            if (dto.Destinatarios == null || !dto.Destinatarios.Any(d => !string.IsNullOrWhiteSpace(d)))
            {
                return BadRequest("Hay que indicar al menos un destinatario.");
            }
            if (string.IsNullOrWhiteSpace(dto.Asunto))
            {
                return BadRequest("Hay que indicar un asunto.");
            }

            using (MailMessage mail = new MailMessage())
            {
                try
                {
                    string remitente = string.IsNullOrWhiteSpace(dto.Remitente) ? REMITENTE_DEFECTO : dto.Remitente;
                    string nombreRemitente = string.IsNullOrWhiteSpace(dto.NombreRemitente) ? NOMBRE_REMITENTE_DEFECTO : dto.NombreRemitente;
                    mail.From = new MailAddress(remitente, nombreRemitente);

                    foreach (string destinatario in dto.Destinatarios.Where(d => !string.IsNullOrWhiteSpace(d)))
                    {
                        mail.To.Add(destinatario);
                    }
                    if (dto.CopiaOculta != null)
                    {
                        foreach (string bcc in dto.CopiaOculta.Where(d => !string.IsNullOrWhiteSpace(d)))
                        {
                            mail.Bcc.Add(bcc);
                        }
                    }
                }
                catch (FormatException ex)
                {
                    return BadRequest("Dirección de correo no válida: " + ex.Message);
                }

                mail.Subject = dto.Asunto;
                mail.Body = dto.Cuerpo ?? string.Empty;
                mail.IsBodyHtml = dto.EsHtml;

                if (dto.Adjuntos != null)
                {
                    foreach (AdjuntoCorreoDTO adjuntoDto in dto.Adjuntos)
                    {
                        if (adjuntoDto == null || string.IsNullOrWhiteSpace(adjuntoDto.ContenidoBase64))
                        {
                            return BadRequest("Cada adjunto debe llevar contenido en base64.");
                        }
                        byte[] bytes;
                        try
                        {
                            bytes = Convert.FromBase64String(adjuntoDto.ContenidoBase64);
                        }
                        catch (FormatException)
                        {
                            return BadRequest($"El adjunto '{adjuntoDto.Nombre}' no es un base64 válido.");
                        }
                        string nombre = string.IsNullOrWhiteSpace(adjuntoDto.Nombre) ? "adjunto" : adjuntoDto.Nombre;
                        Attachment attachment = new Attachment(new MemoryStream(bytes), nombre);
                        if (!string.IsNullOrWhiteSpace(adjuntoDto.TipoMime))
                        {
                            attachment.ContentType = new ContentType(adjuntoDto.TipoMime);
                        }
                        mail.Attachments.Add(attachment);
                    }
                }

                bool enviado = _servicio.EnviarCorreoSMTP(mail);
                if (!enviado)
                {
                    return Content(HttpStatusCode.InternalServerError, new EnvioCorreoRespuestaDTO
                    {
                        Enviado = false,
                        Mensaje = "No se pudo enviar el correo por SMTP."
                    });
                }

                return Ok(new EnvioCorreoRespuestaDTO
                {
                    Enviado = true,
                    Mensaje = "Correo enviado correctamente."
                });
            }
        }
    }
}
