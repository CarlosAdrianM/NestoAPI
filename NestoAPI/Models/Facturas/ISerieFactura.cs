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
    }
}
