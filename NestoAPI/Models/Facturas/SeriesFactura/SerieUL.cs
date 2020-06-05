using System.Collections.Generic;
using System.Net.Mail;

namespace NestoAPI.Models.Facturas.SeriesFactura
{
    public class SerieUL : ISerieFactura
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

    }
}