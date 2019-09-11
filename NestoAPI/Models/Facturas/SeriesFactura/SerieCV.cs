using System.Collections.Generic;

namespace NestoAPI.Models.Facturas.SeriesFactura
{
    public class SerieCV : ISerieFactura
    {

        public string RutaInforme => @"Models\Facturas\Factura.rdlc";

        public List<NotaFactura> Notas => new List<NotaFactura>
            {
                new NotaFactura{ Nota = "EL PLAZO MÁXIMO PARA CUALQUIER RECLAMACIÓN DE ESTE PEDIDO ES DE 24 HORAS." },
                new NotaFactura{ Nota = "LA ASISTENCIA A CLASE ESTÁ SUPEDITADA A ENCONTRARSE AL CORRIENTE DE PAGO." }
            };
    }
}