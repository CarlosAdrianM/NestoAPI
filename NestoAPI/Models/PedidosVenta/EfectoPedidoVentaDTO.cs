using System;

namespace NestoAPI.Models.PedidosVenta
{
    public class EfectoPedidoVentaDTO
    {
        public int Id { get; set; }
        public DateTime FechaVencimiento { get; set; }
        public decimal Importe { get; set; }
        public string FormaPago { get; set; }
        public string Ccc { get; set; }
    }
}