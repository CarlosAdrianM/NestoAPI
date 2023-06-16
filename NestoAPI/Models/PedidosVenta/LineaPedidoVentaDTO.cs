using NestoAPI.Models.PedidosBase;
using System;

namespace NestoAPI.Models.PedidosVenta
{
    public class LineaPedidoVentaDTO : LineaPedidoBase
    {
        public int id { get; set; }
        public string almacen { get; set; }
        public bool aplicarDescuento { get; set; }
        public short cantidad { get; set; } // era Nullable<short> 
        public string delegacion { get; set; }
        public decimal descuento { get; set; }
        public short estado { get; set; }
        public System.DateTime fechaEntrega { get; set; }
        public string formaVenta { get; set; }
        public string iva { get; set; }
        public Nullable<int> oferta { get; set; }
        public int picking { get; set; }
        public decimal precio { get; set; } // era Nullable<decimal> 
        //public string Producto { get; set; }
        public string texto { get; set; }
        public Nullable<byte> tipoLinea { get; set; }
        public string usuario { get; set; }
        public bool vistoBueno { get; set; }
        //public decimal BaseImponible { get; set; }
        //public decimal ImporteIva { get; set; }
        //public decimal Total { get; set; }
        public decimal descuentoProducto { get; set; }
        public decimal precioTarifa { get; set; }
    }

}