using System.Collections.Generic;
using System.Net.Mail;

namespace NestoAPI.Models.Facturas.SeriesFactura
{
    // Decisión Carlos 17/08/26 (#39): la serie se MANTIENE (marca Unión Láser, misma lógica que
    // EV) y pasa a tramitar Verifactu: son ventas reales que estaban quedando sin declarar.
    public class SerieUL : ISerieFacturaVerifactu
    {
        public string RutaInforme => @"Models\Facturas\FacturaUL.rdlc";

        public List<NotaFactura> Notas => new List<NotaFactura>
            {
                new NotaFactura{ Nota = "EL PLAZO MÁXIMO PARA CUALQUIER RECLAMACIÓN DE ESTE PEDIDO ES DE 24 HORAS." },
                new NotaFactura{ Nota = "LOS GASTOS POR DEVOLUCIÓN DEL PRODUCTO SERÁN SIEMPRE A CARGO DEL CLIENTE." }
            };
        public MailAddress CorreoDesdeFactura => new MailAddress("facturacion@unionlaser.es", "UNIÓN LÁSER");
        public MailAddress CorreoDesdeLogistica => new MailAddress("logistica@unionlaser.es", "UNIÓN LÁSER");
        public string FirmaCorreo => "<p>Departamento de Administración<br/>Tel. 647505622<br/>facturacion@unionlaser.es</p>";

        // Propiedades QuestPDF
        public string UrlLogo => "https://unionlaser.es/img/union-laser-logo-1449150245.jpg";
        public bool EsDescargable => true;
        public bool EsImprimible => true;
        public bool UsaFormatoTicket => false;

        // Propiedades Verifactu
        public bool TramitaVerifactu => true;
        public string TipoFacturaVerifactuPorDefecto => "F1";
        public bool EsRectificativa => false;
        public string DescripcionVerifactu => "Venta de productos Unión Láser";
        public string SerieRectificativaAsociada => "RV";
    }
}