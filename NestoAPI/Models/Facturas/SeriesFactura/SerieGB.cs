using System.Collections.Generic;

namespace NestoAPI.Models.Facturas.SeriesFactura
{
    public class SerieGB : ISerieFactura
    {
        public string RutaInforme => @"Models\Facturas\FacturaGB.rdlc";

        public List<NotaFactura> Notas => new List<NotaFactura>();
    }
}