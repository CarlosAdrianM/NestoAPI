using System.Collections.Generic;
using System.Net.Mail;
using NestoAPI.Models.Facturas;

namespace NestoAPI.Models.Facturas.SeriesFactura
{
    public class SerieCV : ISerieFacturaVerifactu
    {
        public string RutaInforme => @"Models\Facturas\Factura.rdlc";

        public List<NotaFactura> Notas => new List<NotaFactura>
            {
                new NotaFactura{ Nota = "Operación exenta de IVA según el artículo 20.1.9º de la Ley 37/1992 del Impuesto sobre el Valor Añadido." },
                new NotaFactura{ Nota = "" },
                new NotaFactura{ Nota = "LA ASISTENCIA A CLASE ESTÁ SUPEDITADA A ENCONTRARSE AL CORRIENTE DE PAGO." }
            };

        public MailAddress CorreoDesdeFactura => new MailAddress("administracion@nuevavision.es", "CURSOS NUEVA VISIÓN");
        public MailAddress CorreoDesdeLogistica => new MailAddress("logistica@nuevavision.es", "LOGÍSTICA CURSOS NUEVA VISIÓN");

        public string FirmaCorreo => "<p>Departamento de Formación y Cursos<br/>Tel. 915311923<br/>cursos@nuevavision.es</p>";

        // Propiedades QuestPDF (usa el mismo logo que NV)
        public string UrlLogo => "https://www.productosdeesteticaypeluqueriaprofesional.com/img/cms/Landing/logo.png";
        public bool EsDescargable => true;
        public bool EsImprimible => true;
        public bool UsaFormatoTicket => false;

        // Propiedades Verifactu
        public bool TramitaVerifactu => true;
        public string TipoFacturaVerifactuPorDefecto => "F1";
        public bool EsRectificativa => false;
        public string DescripcionVerifactu => "Servicios de formación";
        public string SerieRectificativaAsociada => "RC";
    }
}