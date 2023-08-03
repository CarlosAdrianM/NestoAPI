using NestoAPI.Models.PedidosBase;
using Newtonsoft.Json;
using System;

namespace NestoAPI.Models.PedidosVenta
{
    public class LineaPedidoVentaDTO : LineaPedidoBase
    {
        [JsonIgnore]
        public PedidoVentaDTO Pedido { get; set; }
        public int id { get; set; }
        public string almacen { get; set; }
        //public bool aplicarDescuento { get; set; }
        //public short Cantidad { get; set; } // era Nullable<short> 
        public string delegacion { get; set; }
        //public decimal descuento { get; set; }
        public short estado { get; set; }
        public System.DateTime fechaEntrega { get; set; }
        public string formaVenta { get; set; }
        public string iva { get; set; }
        public Nullable<int> oferta { get; set; }
        public int picking { get; set; }
        //public decimal PrecioUnitario { get; set; } // era Nullable<decimal> 
        //public string Producto { get; set; }
        public string texto { get; set; }
        public Nullable<byte> tipoLinea { get; set; }
        public string usuario { get; set; }
        public bool vistoBueno { get; set; }
        //public decimal BaseImponible { get; set; }
        //public decimal ImporteIva { get; set; }
        //public decimal Total { get; set; }
        //public decimal descuentoProducto { get; set; }
        public decimal precioTarifa { get; set; }

        public override decimal SumaDescuentos {
            get
            {
                decimal descuentoPP;
                if (Pedido == null)
                {
                    descuentoPP = 0;
                } else
                {
                    descuentoPP = Pedido.DescuentoPP;
                }
                return AplicarDescuento ? 1 - (1 - DescuentoEntidad) * (1 - DescuentoProducto) * (1 - DescuentoLinea) * (1 - descuentoPP) : 1 - (1 - DescuentoLinea) * (1 - descuentoPP);
            }
        }
    }

}