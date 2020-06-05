using System.Collections.Generic;
using System.Net.Mail;

namespace NestoAPI.Models.Facturas.SeriesFactura
{
    public class SerieCV : ISerieFactura
    {

        public string RutaInforme => @"Models\Facturas\Factura.rdlc";

        public List<NotaFactura> Notas => new List<NotaFactura>
            {
                new NotaFactura{ Nota = "LA ASISTENCIA A CLASE ESTÁ SUPEDITADA A ENCONTRARSE AL CORRIENTE DE PAGO." }
            };

        public MailAddress CorreoDesdeFactura => new MailAddress("administracion@nuevavision.es", "CURSOS NUEVA VISIÓN");
        public MailAddress CorreoDesdeLogistica => new MailAddress("logistica@nuevavision.es", "LOGÍSTICA CURSOS NUEVA VISIÓN");

        public string FirmaCorreo => "<p>Departamento de Formación y Cursos<br/>Tel. 915311923<br/>cursos@nuevavision.es</p>";
    }
}