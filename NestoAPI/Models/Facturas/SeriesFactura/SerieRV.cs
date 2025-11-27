using System.Collections.Generic;
using System.Net.Mail;
using NestoAPI.Models.Facturas;

namespace NestoAPI.Models.Facturas.SeriesFactura
{
    /// <summary>
    /// Serie RV: Rectificativas de facturas de venta (serie NV).
    /// Tipos de rectificativa: R1 (devolución), R3 (impago), R4 (error).
    /// </summary>
    public class SerieRV : ISerieFacturaVerifactu
    {
        public string RutaInforme => @"Models\Facturas\Factura.rdlc";

        public List<NotaFactura> Notas => new List<NotaFactura>
            {
                new NotaFactura{ Nota = "FACTURA RECTIFICATIVA" },
                new NotaFactura{ Nota = "EL PLAZO MÁXIMO PARA CUALQUIER RECLAMACIÓN DE ESTE PEDIDO ES DE 24 HORAS." }
            };

        public MailAddress CorreoDesdeFactura => new MailAddress("administracion@nuevavision.es");
        public MailAddress CorreoDesdeLogistica => new MailAddress("logistica@nuevavision.es");

        public string FirmaCorreo => "<p>Departamento de Administración<br/>Tel. 916281914<br/>administracion@nuevavision.es</p>";

        // Propiedades Verifactu
        public bool TramitaVerifactu => true;
        public string TipoFacturaVerifactuPorDefecto => "R1"; // Devolución por defecto
        public bool EsRectificativa => true;
        public string DescripcionVerifactu => "Rectificación de factura";
        public string SerieRectificativaAsociada => null; // Ya es rectificativa
    }
}
