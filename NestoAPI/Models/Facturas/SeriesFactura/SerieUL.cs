using System.Collections.Generic;

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
    }
}