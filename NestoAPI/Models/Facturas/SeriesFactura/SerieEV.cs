using System.Collections.Generic;
using System.Net.Mail;

namespace NestoAPI.Models.Facturas.SeriesFactura
{
    public class SerieEV : ISerieFactura
    {
        public string RutaInforme => @"Models\Facturas\FacturaVC.rdlc";

        public List<NotaFactura> Notas => new List<NotaFactura>
            {
                new NotaFactura{ Nota = "EL PLAZO MÁXIMO PARA CUALQUIER RECLAMACIÓN DE ESTE PEDIDO ES DE 24 HORAS." },
                new NotaFactura{ Nota = "LOS GASTOS POR DEVOLUCIÓN DEL PRODUCTO SERÁN SIEMPRE A CARGO DEL CLIENTE." }
            };
        public MailAddress CorreoDesdeFactura => new MailAddress("administracion@evavisnu.com", "EVA VISNÚ");
        public MailAddress CorreoDesdeLogistica => new MailAddress("logistica@evavisnu.com", "EVA VISNÚ");
        public string FirmaCorreo => "<p>Departamento de Administración<br/>Tel. 916281216<br/>administracion@evavisnu.com</p>";

        // Propiedades QuestPDF
        public string UrlLogo => "https://www.evavisnu.com/img/nueva-vision-sa-logo-1490174942.jpg";
        public bool EsDescargable => true;
        public bool EsImprimible => true;
        public bool UsaFormatoTicket => false;
    }
}