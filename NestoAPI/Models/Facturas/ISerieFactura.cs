using System.Collections.Generic;
using System.Net.Mail;

namespace NestoAPI.Models.Facturas
{
    public interface ISerieFactura
    {
        string RutaInforme { get; }
        List<NotaFactura> Notas { get; }
        MailAddress CorreoDesde { get; }
        string FirmaCorreo { get; }
    }
}
