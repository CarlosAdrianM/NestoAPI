using System.Collections.Generic;
using System.Net.Mail;

namespace NestoAPI.Models.Facturas.SeriesFactura
{
    // Decisión Carlos 17/08/26 (#39): la serie se MANTIENE (marca Eva Visnú para distribuidores;
    // las series por marca son legales, RD 1619/2012 art. 6.1.a) y pasa a tramitar Verifactu:
    // son ventas reales que estaban quedando sin declarar.
    public class SerieEV : ISerieFacturaVerifactu
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

        // Propiedades Verifactu
        public bool TramitaVerifactu => true;
        public string TipoFacturaVerifactuPorDefecto => "F1";
        public bool EsRectificativa => false;
        public string DescripcionVerifactu => "Venta de productos Eva Visnú";
        // DV deja de usarse: los abonos de EV van a la serie rectificativa común
        public string SerieRectificativaAsociada => "RV";
    }
}