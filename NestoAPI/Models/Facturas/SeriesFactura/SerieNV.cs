using System.Collections.Generic;
using System.Net.Mail;
using NestoAPI.Models.Facturas;

namespace NestoAPI.Models.Facturas.SeriesFactura
{
    public class SerieNV : ISerieFacturaVerifactu
    {
        public string RutaInforme => @"Models\Facturas\Factura.rdlc";

        public List<NotaFactura> Notas => new List<NotaFactura>
            {
                new NotaFactura{ Nota = "EL PLAZO MÁXIMO PARA CUALQUIER RECLAMACIÓN DE ESTE PEDIDO ES DE 24 HORAS." },
                new NotaFactura{ Nota = "LOS GASTOS POR DEVOLUCIÓN DEL PRODUCTO SERÁN SIEMPRE A CARGO DEL CLIENTE." }
            };
        public MailAddress CorreoDesdeFactura => new MailAddress("administracion@nuevavision.es");
        public MailAddress CorreoDesdeLogistica => new MailAddress("logistica@nuevavision.es");

        public string FirmaCorreo => "<p>Departamento de Administración<br/>Tel. 916281914<br/>administracion@nuevavision.es</p>";

        // Propiedades Verifactu
        public bool TramitaVerifactu => true;
        public string TipoFacturaVerifactuPorDefecto => "F1";
        public bool EsRectificativa => false;
        public string DescripcionVerifactu => "Venta de productos";
        public string SerieRectificativaAsociada => "RV";
    }
}