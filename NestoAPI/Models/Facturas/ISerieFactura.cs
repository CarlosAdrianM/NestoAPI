using System.Collections.Generic;

namespace NestoAPI.Models.Facturas
{
    public interface ISerieFactura
    {
        string RutaInforme { get; }
        List<NotaFactura> Notas { get; }
    }
}
