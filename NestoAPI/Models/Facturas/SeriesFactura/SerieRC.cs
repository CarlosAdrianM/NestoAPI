using System.Collections.Generic;
using System.Net.Mail;
using NestoAPI.Models.Facturas;

namespace NestoAPI.Models.Facturas.SeriesFactura
{
    /// <summary>
    /// Serie RC: Rectificativas de facturas de cursos/formación (serie CV).
    /// Mantiene la exención de IVA para el cálculo correcto de la prorrata.
    /// Tipos de rectificativa: R1 (devolución), R3 (impago), R4 (error).
    /// </summary>
    public class SerieRC : ISerieFacturaVerifactu
    {
        public string RutaInforme => @"Models\Facturas\Factura.rdlc";

        public List<NotaFactura> Notas => new List<NotaFactura>
            {
                new NotaFactura{ Nota = "FACTURA RECTIFICATIVA" },
                new NotaFactura{ Nota = "Operación exenta de IVA según el artículo 20.1.9º de la Ley 37/1992 del Impuesto sobre el Valor Añadido." },
                new NotaFactura{ Nota = "" }
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
        public string TipoFacturaVerifactuPorDefecto => "R1"; // Devolución por defecto
        public bool EsRectificativa => true;
        public string DescripcionVerifactu => "Rectificación de servicios de formación";
        public string SerieRectificativaAsociada => null; // Ya es rectificativa
    }
}
