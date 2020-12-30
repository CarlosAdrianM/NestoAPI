using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Infraestructure.Agencias
{
    public class GestorEnviosAgencia
    {
        public async Task EnviarCorreoEntregaAgencia(EnviosAgencia envio)
        {
            if (envio == null || string.IsNullOrWhiteSpace(envio.Email)) {
                return;
            }

            if (envio.Cliente == Constantes.ClientesEspeciales.TIENDA_ONLINE || envio.Cliente == Constantes.ClientesEspeciales.AMAZON)
            {
                return;
            }

            GestorFacturas gestorFacturas = new GestorFacturas();
            FacturaLookup factura = new FacturaLookup { Empresa = envio.Empresa, Factura = envio.Pedido.ToString() };
            List<FacturaLookup> lista = new List<FacturaLookup>
            {
                factura
            };
            List<Factura> facturas = gestorFacturas.LeerFacturas(lista);
            var facturaPdf = gestorFacturas.FacturasEnPDF(facturas);
            Attachment attachment = new Attachment(new MemoryStream(await facturaPdf.ReadAsByteArrayAsync()), envio.Pedido.ToString() + ".pdf");

            MailMessage mail = new MailMessage();
            try
            {
                ServicioFacturas servicio = new ServicioFacturas();
                CabPedidoVta cabEspejo = servicio.CargarCabPedido(Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO, (int)envio.Pedido);
                CabPedidoVta cabPedido = servicio.CargarCabPedido(envio.Empresa, (int)envio.Pedido);
                if (cabEspejo != null || cabPedido.IVA == null)
                {
                    return;
                }
                ISerieFactura serieFactura = GestorFacturas.LeerSerie(cabPedido.Serie);
                mail.From = serieFactura.CorreoDesdeLogistica;
                mail.To.Add(new MailAddress(envio.Email));
                mail.Bcc.Add(new MailAddress("carlosadrian@nuevavision.es"));
                mail.Subject = string.Format("Pedido entregado a la agencia ({0}/{1})", envio.Cliente.Trim(), envio.Pedido.ToString());
            }
            catch
            {
                mail.To.Add(new MailAddress(Constantes.Correos.LOGISTICA));
                mail.Subject = String.Format("[ERROR: {0}] Pedido entregado a la agencia ({1}/{2})", envio.Email, envio.Cliente.Trim(), envio.Pedido.ToString());
            }
            
            mail.Body = (await GenerarCorreoHTML(envio)).ToString();
            mail.IsBodyHtml = true;
            mail.Attachments.Add(attachment);
            SmtpClient client = new SmtpClient();
            client.Port = 587;
            client.EnableSsl = true;
            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            client.UseDefaultCredentials = false;
            string contrasenna = ConfigurationManager.AppSettings["office365password"];
            client.Credentials = new System.Net.NetworkCredential("nesto@nuevavision.es", contrasenna);
            client.Host = "smtp.office365.com";
            client.Send(mail);
            mail.Dispose();
        }

        private async Task<StringBuilder> GenerarCorreoHTML(EnviosAgencia envio)
        {
            ServicioEnviosAgencia servicio = new ServicioEnviosAgencia();
            string nombreAgencia = servicio.LeerAgencia(envio.Agencia).Nombre;
            EnvioAgenciaDTO envioDTO = new EnvioAgenciaDTO(envio);
            envioDTO.AgenciaNombre = nombreAgencia;
            StringBuilder s = new StringBuilder();

            s.AppendLine("<h3>¡Hola!</h3>");
            s.AppendLine("<p>Le comunicamos que ya hemos enviado su pedido. Debido a que este pedido ya se encuentra en poder de la agencia de transportes, a partir de este momento no se puede realizar ninguna modificación en él. ");
            s.AppendLine("El pedido ya está en camino y por lo tanto no se puede modificar.</p>");
            s.AppendLine("<p>La propia agencia le enviará un correo electrónico a esta misma dirección con el enlace al seguimiento de la expedición, para que pueda saber en cada momento por donde va el envío.</p>");
            s.AppendLine("<p>No obstante, le adelantamos que <b>la agencia responsable de la entrega es "+ nombreAgencia +" y el número de envío es <a href=\""+envioDTO.EnlaceSeguimiento+"\">" +envio.CodigoBarras+"</a> </b>");
            s.AppendLine("(es posible que el enlace tarde un rato en estar operativo).</p>");
            s.AppendLine("<p>Adjunto encontrará un PDF con el pedido completo, en el que hemos marcado <span style=\"color: red;\">en rojo las líneas pendientes de enviar y facturar</span>, que se le enviarán tan ");
            s.AppendLine("pronto como tengamos stock y <span style=\"color: green;\">en verde las que enviamos en esta expedición</span>.</p>");

            return s;
        }
    }
}