using System.Collections.Generic;
using System.Net.Mail;

namespace NestoAPI.Models.Facturas
{
    public interface ISerieFactura
    {
        string RutaInforme { get; }
        List<NotaFactura> Notas { get; }
        MailAddress CorreoDesdeFactura { get; }
        MailAddress CorreoDesdeLogistica { get; }
        string FirmaCorreo { get; }

        /// <summary>
        /// URL del logo para generación de PDFs con QuestPDF.
        /// Null si la serie no tiene logo (ej: GB).
        /// </summary>
        string UrlLogo { get; }

        /// <summary>
        /// Indica si las facturas de esta serie se pueden descargar como PDF.
        /// Por defecto true.
        /// </summary>
        bool EsDescargable { get; }

        /// <summary>
        /// Indica si las facturas de esta serie se pueden imprimir.
        /// Por defecto true.
        /// </summary>
        bool EsImprimible { get; }

        /// <summary>
        /// Indica si esta serie usa formato ticket en lugar de factura estándar.
        /// El formato ticket es simplificado: sin logo, sin desglose IVA, sin vencimientos.
        /// Por defecto false.
        /// </summary>
        bool UsaFormatoTicket { get; }
    }
}
