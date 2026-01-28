using System.Collections.Generic;
using System.Net.Mail;

namespace NestoAPI.Models.Facturas.SeriesFactura
{
    public class SerieGB : ISerieFactura
    {
        // GestorBuy - Serie especial que NO permite envío de facturas por correo electrónico
        // pero SÍ permite impresión física en facturación de rutas

        public string RutaInforme => @"Models\Facturas\FacturaGB.rdlc";

        public List<NotaFactura> Notas => new List<NotaFactura>();

        // IMPORTANTE: null indica que esta serie NO permite envío por correo electrónico
        public MailAddress CorreoDesdeFactura => null;

        public string FirmaCorreo => null;

        public MailAddress CorreoDesdeLogistica => null;

        // Propiedades QuestPDF
        // GB usa formato ticket sin logo
        public string UrlLogo => null;
        public bool EsDescargable => false;  // No permite descarga de PDFs
        public bool EsImprimible => true;    // Sí permite impresión física
        public bool UsaFormatoTicket => true; // Formato simplificado tipo ticket
    }
}