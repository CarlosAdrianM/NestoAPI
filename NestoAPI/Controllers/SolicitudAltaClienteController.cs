using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Net.Mail;
using System.Text;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Recibe solicitudes de alta como cliente desde TiendasNuevaVision (TiendasNuevaVision#14).
    /// Usuarios que todavía no son clientes (y por tanto no pueden hacer login) dejan sus datos
    /// y se envía un correo a la tienda online para contactarles.
    /// </summary>
    [RoutePrefix("api/SolicitudAltaCliente")]
    public class SolicitudAltaClienteController : ApiController
    {
        private readonly IServicioCorreoElectronico _servicioCorreo;

        public SolicitudAltaClienteController() : this(new ServicioCorreoElectronico()) { }

        public SolicitudAltaClienteController(IServicioCorreoElectronico servicioCorreo)
        {
            _servicioCorreo = servicioCorreo;
        }

        [HttpPost]
        [Route("")]
        // AllowAnonymous a propósito: la solicitud la envían usuarios que aún no son
        // clientes y no tienen forma de autenticarse
        [AllowAnonymous]
        public IHttpActionResult Post([FromBody] SolicitudAltaClienteDTO solicitud)
        {
            if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.Email))
            {
                return BadRequest("El correo electrónico es obligatorio");
            }

            MailAddress remitente;
            try
            {
                // Valida de paso el formato del email que nos han dado
                remitente = new MailAddress(solicitud.Email.Trim());
            }
            catch (FormatException)
            {
                return BadRequest("El correo electrónico no tiene un formato válido");
            }

            StringBuilder cuerpo = new StringBuilder();
            _ = cuerpo.AppendLine("Solicitud de alta como cliente desde la app Tiendas Nueva Visión:");
            _ = cuerpo.AppendLine();
            _ = cuerpo.AppendLine($"Email: {remitente.Address}");
            _ = cuerpo.AppendLine($"NIF: {solicitud.Nif?.Trim()}");
            _ = cuerpo.AppendLine($"Teléfono: {solicitud.Telefono?.Trim()}");
            _ = cuerpo.AppendLine($"País: {solicitud.Pais?.Trim()}");
            _ = cuerpo.AppendLine($"Código postal: {solicitud.CodigoPostal?.Trim()}");
            _ = cuerpo.AppendLine($"Comentarios: {solicitud.Comentarios?.Trim()}");

            using (MailMessage mail = new MailMessage())
            {
                mail.From = new MailAddress("nesto@nuevavision.es", "Tiendas Nueva Visión");
                mail.To.Add(Constantes.Correos.TIENDA_ONLINE);
                mail.ReplyToList.Add(remitente);
                mail.Subject = $"Solicitud de alta como cliente: {remitente.Address}";
                mail.Body = cuerpo.ToString();

                bool enviado = _servicioCorreo.EnviarCorreoSMTP(mail);
                return enviado
                    ? (IHttpActionResult)Ok()
                    : InternalServerError(new Exception("No se pudo enviar la solicitud"));
            }
        }
    }
}
