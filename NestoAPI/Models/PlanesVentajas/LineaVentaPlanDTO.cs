using System;

namespace NestoAPI.Models.PlanesVentajas
{
    public class LineaVentaPlanDTO
    {
        public int NumeroPedido { get; set; }
        public string Producto { get; set; }
        public string Texto { get; set; }
        public short? Cantidad { get; set; }
        public decimal BaseImponible { get; set; }
        public DateTime? FechaFactura { get; set; }
    }
}
