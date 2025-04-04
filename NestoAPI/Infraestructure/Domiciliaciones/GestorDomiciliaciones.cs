using NestoAPI.Models;
using NestoAPI.Models.Domiciliaciones;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;

namespace NestoAPI.Infraestructure.Domiciliaciones
{
    public class GestorDomiciliaciones
    {
        private readonly IServicioDomiciliaciones servicio;
        private readonly IServicioCorreoElectronico servicioCorreo;
        public GestorDomiciliaciones(IServicioDomiciliaciones servicio, IServicioCorreoElectronico servicioCorreo)
        {
            this.servicio = servicio;
            this.servicioCorreo = servicioCorreo;
        }
        public IEnumerable<EfectoDomiciliado> EnviarCorreoDomiciliacion(DateTime dia)
        {
            ICollection<EfectoDomiciliado> efectosDomiciliados = servicio.LeerDomiciliacionesDia(dia);
            List<DomiciliacionesCliente> clientesConEfectos = AgruparPorCliente(efectosDomiciliados);

            List<MailMessage> listaCorreos = new List<MailMessage>();
            foreach (DomiciliacionesCliente cliente in clientesConEfectos)
            {
                MailMessage correo = new MailMessage();
                try
                {
                    correo.From = new MailAddress(Constantes.Correos.CORREO_ADMON, "NUEVA VISIÓN (Administración)");
                    correo.To.Add(cliente.Correo);
                    correo.Subject = $"Información de cargo por domiciliación bancaria (cliente {cliente.ListaEfectos[0].Cliente})";
                }
                catch
                {
                    correo.To.Add(new MailAddress(Constantes.Correos.CORREO_ADMON));
                    correo.Subject = $"[ERROR {cliente.Correo}] Información de cargo por domiciliación bancaria (cliente {cliente.ListaEfectos[0].Cliente})";
                }
                correo.Bcc.Add("carlosadrian@nuevavision.es");
                correo.Bcc.Add("lauramagan@nuevavision.es");
                correo.IsBodyHtml = true;
                correo.Body = GenerarCorreoDomiciliacionHTML(cliente);
                listaCorreos.Add(correo);
            }
            foreach (MailMessage correo in listaCorreos)
            {
                _ = servicioCorreo.EnviarCorreoSMTP(correo);
            }
            return efectosDomiciliados;
        }

        private string GenerarCorreoDomiciliacionHTML(DomiciliacionesCliente cliente)
        {
            StringBuilder s = new StringBuilder();

            _ = s.AppendLine("<!DOCTYPE html>");
            _ = s.AppendLine("<html lang='es'>");
            _ = s.AppendLine("<head>");
            _ = s.AppendLine("<meta http-equiv='Content-Type' content='text/html; charset=utf-8' />");
            _ = s.AppendLine("<meta name='viewport' content='width=device-width'>");
            _ = s.AppendLine("<title>Información de cargo por domiciliación bancaria</title>");
            _ = s.AppendLine("<style></style>");
            _ = s.AppendLine("</head>");

            _ = s.AppendLine("<body>");
            //s.AppendLine("<div id='email' style='width:600px;'>");

            //<!-- Header --> 
            _ = s.AppendLine("<table role='presentation' border='0' cellspacing='0' width='100%'>");
            _ = s.AppendLine("<tr>");
            _ = s.AppendLine("<td width=\"50%\" align='left'><img src=\"http://www.productosdeesteticaypeluqueriaprofesional.com/logofra.jpg\"></td>");
            _ = s.AppendLine("<td width=\"50%\" align='right'><img src=\"https://www.sepaesp.es/f/websepa/INF/assets/img/logo.jpg\"></td>");
            _ = s.AppendLine("</tr>");
            _ = s.AppendLine("</table>");

            //<!-- Body --> 
            if (!string.IsNullOrWhiteSpace(cliente.ListaEfectos[0].NombrePersona))
            {
                _ = s.AppendLine("<p>Buenas tardes " + cliente.ListaEfectos[0].NombrePersona + ":</p>");
            }
            else
            {
                _ = s.AppendLine("<p>Estimado cliente:</p>");
            }
            _ = s.AppendLine("<p>Le informamos que, al haber llegado ya la fecha de vencimiento, hemos enviado a su banco los siguientes efectos para que lleven a cabo la gestión de cobro:</p>");
            _ = s.AppendLine("<table role='presentation' border='0' cellspacing='0' width='100%'>");
            _ = s.AppendLine("<tr>");
            _ = s.AppendLine("<td align='left' style='border: 1px solid black;color:#333;padding:3px'>Datos de los efectos</td>");
            _ = s.AppendLine("</tr>");
            foreach (EfectoDomiciliado efecto in cliente.ListaEfectos)
            {
                _ = s.AppendLine("<tr>");
                _ = s.AppendLine("<td style='border: 1px solid black;color:#333;padding:3px'>");
                _ = s.AppendLine("<ul>");
                _ = s.AppendLine("<li>Concepto: " + efecto.Concepto + "</li>");
                _ = s.AppendLine("<li>Importe: " + efecto.Importe.ToString("c") + "</li>");
                _ = s.AppendLine("<li>IBAN: " + efecto.Iban.Enmascarado + "</li>");
                _ = s.AppendLine("</ul>");
                _ = s.AppendLine("</td>");
                _ = s.AppendLine("</tr>");
            }
            _ = s.AppendLine("</table>");
            _ = s.AppendLine("<p style=\"color: blue;\"><strong>Este es un correo automatizado y con carácter meramente informativo, para que usted pueda tener controlados los recibos que le hemos girado.</strong></p>");
            _ = s.AppendLine("<p>El tiempo que tarde el banco en cargar los recibos en su cuenta no depende de nosotros, por lo que si precisa información más exacta al respecto, le sugerimos se ponga en contacto con su oficina bancaria.</p>");
            _ = s.AppendLine("<p>Si algo de lo aquí expresado no es de su conformidad o necesita cualquier otra aclaración adicional, puede contactarnos respondiendo a este mismo correo.</p>");

            // Sección para la descarga de la app
            _ = s.AppendLine("<p><strong>Si necesita alguna factura de las mencionadas arriba, puede conseguirlas cómodamente desde nuestra aplicación en su móvil.</strong></p>");
            _ = s.AppendLine("<p>Descárguela ahora desde Google Play:</p>");
            _ = s.AppendLine("<a href=\"https://play.google.com/store/apps/details?id=com.nuevavision.nestotiendas\" target=\"_blank\">");
            _ = s.AppendLine("<img src=\"https://upload.wikimedia.org/wikipedia/commons/7/78/Google_Play_Store_badge_EN.svg\" alt=\"Disponible en Google Play\" style=\"width: 150px; height: auto;\" />");
            _ = s.AppendLine("</a>");
            _ = s.AppendLine("<br/>");

            //<!-- Footer -->
            _ = s.AppendLine("<table role='presentation' border='0' cellspacing='0' width='100%'>");
            _ = s.AppendLine("</table>");
            //s.AppendLine("</div>");
            _ = s.AppendLine("</body>");



            return s.ToString();
        }

        private List<DomiciliacionesCliente> AgruparPorCliente(ICollection<EfectoDomiciliado> efectosDomiciliados)
        {
            List<DomiciliacionesCliente> domiciliacionesClientes = new List<DomiciliacionesCliente>();
            DomiciliacionesCliente domiciliacion = new DomiciliacionesCliente();
            string ultimoCorreo = string.Empty;
            foreach (EfectoDomiciliado efecto in efectosDomiciliados.Where(e => !string.IsNullOrEmpty(e.Correo)).OrderBy(e => e.Correo))
            {
                if (string.IsNullOrEmpty(ultimoCorreo))
                {
                    ultimoCorreo = efecto.Correo;
                    domiciliacion.Correo = ultimoCorreo;
                }
                if (ultimoCorreo != efecto.Correo)
                {
                    domiciliacionesClientes.Add(domiciliacion);
                    domiciliacion = new DomiciliacionesCliente
                    {
                        Correo = efecto.Correo
                    };
                }
                domiciliacion.ListaEfectos.Add(efecto);
                ultimoCorreo = efecto.Correo;
            }
            domiciliacion.Correo = ultimoCorreo;
            domiciliacionesClientes.Add(domiciliacion);
            return domiciliacionesClientes;
        }
    }
}