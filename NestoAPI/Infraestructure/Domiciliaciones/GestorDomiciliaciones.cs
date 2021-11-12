using NestoAPI.Models;
using NestoAPI.Models.Domiciliaciones;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Infraestructure.Domiciliaciones
{
    public class GestorDomiciliaciones
    {
        IServicioDomiciliaciones servicio;
        IServicioCorreoElectronico servicioCorreo;
        public GestorDomiciliaciones(IServicioDomiciliaciones servicio, IServicioCorreoElectronico servicioCorreo)
        {
            this.servicio = servicio;
            this.servicioCorreo = servicioCorreo;
        }
        public IEnumerable<EfectoDomiciliado> EnviarCorreoDomiciliacion(DateTime dia)
        {
            var efectosDomiciliados = servicio.LeerDomiciliacionesDia(dia);
            List<DomiciliacionesCliente> clientesConEfectos = AgruparPorCliente(efectosDomiciliados);

            var listaCorreos = new List<MailMessage>();
            foreach (var cliente in clientesConEfectos)
            {
                var correo = new MailMessage();
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
                correo.IsBodyHtml = true;
                correo.Body = GenerarCorreoDomiciliacionHTML(cliente);
                listaCorreos.Add(correo);
            }
            foreach (var correo in listaCorreos)
            {
                servicioCorreo.EnviarCorreoSMTP(correo);
            }
            return efectosDomiciliados;
        }

        private string GenerarCorreoDomiciliacionHTML(DomiciliacionesCliente cliente)
        {
            StringBuilder s = new StringBuilder();

            s.AppendLine("<!DOCTYPE html>");
            s.AppendLine("<html lang='es'>");
            s.AppendLine("<head>");
            s.AppendLine("<meta http-equiv='Content-Type' content='text/html; charset=utf-8' />");
            s.AppendLine("<meta name='viewport' content='width=device-width'>");
            s.AppendLine("<title>Información de cargo por domiciliación bancaria</title>");
            s.AppendLine("<style></style>");
            s.AppendLine("</head>");

            s.AppendLine("<body>");
            //s.AppendLine("<div id='email' style='width:600px;'>");

            //<!-- Header --> 
            s.AppendLine("<table role='presentation' border='0' cellspacing='0' width='100%'>");
            s.AppendLine("<tr>");
            s.AppendLine("<td width=\"50%\" align='left'><img src=\"http://www.productosdeesteticaypeluqueriaprofesional.com/logofra.jpg\"></td>");
            s.AppendLine("<td width=\"50%\" align='right'><img src=\"https://www.sepaesp.es/f/websepa/INF/assets/img/logo.jpg\"></td>");
            s.AppendLine("</tr>");
            s.AppendLine("</table>");

            //<!-- Body --> 
            if (!string.IsNullOrWhiteSpace(cliente.ListaEfectos[0].NombrePersona))
            {
                s.AppendLine("<p>Buenas tardes " + cliente.ListaEfectos[0].NombrePersona + ":</p>");
            }
            else
            {
                s.AppendLine("<p>Estimado cliente:</p>");
            }
            s.AppendLine("<p>Le informamos que, al haber llegado ya la fecha de vencimiento, hemos enviado a su banco los siguientes efectos para que lleven a cabo la gestión de cobro:</p>");
            s.AppendLine("<table role='presentation' border='0' cellspacing='0' width='100%'>");
            s.AppendLine("<tr>");
            s.AppendLine("<td align='left' style='border: 1px solid black;color:#333;padding:3px'>Datos de los efectos</td>");
            s.AppendLine("</tr>");
            foreach (var efecto in cliente.ListaEfectos)
            {
                s.AppendLine("<tr>");
                s.AppendLine("<td style='border: 1px solid black;color:#333;padding:3px'>");
                s.AppendLine("<ul>");
                s.AppendLine("<li>Concepto: " + efecto.Concepto +"</li>");
                s.AppendLine("<li>Importe: " + efecto.Importe.ToString("c") + "</li>");
                s.AppendLine("<li>IBAN: " + efecto.Iban.Enmascarado + "</li>");
                s.AppendLine("</ul>");
                s.AppendLine("</td>");
                s.AppendLine("</tr>");
            }
            s.AppendLine("</table>");
            s.AppendLine("<p style=\"color: blue;\"><strong>Este es un correo automatizado y con carácter meramente informativo, para que usted pueda tener controlados los recibos que le hemos girado.</strong></p>");
            s.AppendLine("<p>El tiempo que tarde el banco en cargar los recibos en su cuenta no depende de nosotros, por lo que si precisa información más exacta al respecto, le sugerimos se ponga en contacto con su oficina bancaria.</p>");
            s.AppendLine("<p>Si algo de lo aquí expresado no es de su conformidad o necesita cualquier otra aclaración adicional, puede contactarnos respondiendo a este mismo correo.</p>");
            

            //<!-- Footer -->
            s.AppendLine("<table role='presentation' border='0' cellspacing='0' width='100%'>");
            s.AppendLine("</table>");
            //s.AppendLine("</div>");
            s.AppendLine("</body>");


            return s.ToString();
        }

        private List<DomiciliacionesCliente> AgruparPorCliente(ICollection<EfectoDomiciliado> efectosDomiciliados)
        {
            List<DomiciliacionesCliente> domiciliacionesClientes = new List<DomiciliacionesCliente>();
            DomiciliacionesCliente domiciliacion = new DomiciliacionesCliente();
            string ultimoCorreo = string.Empty;
            foreach (var efecto in efectosDomiciliados.Where(e => !string.IsNullOrEmpty(e.Correo)).OrderBy(e => e.Correo))
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