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
        public string delegacion { get; set; }
        public short estado { get; set; }
        public System.DateTime fechaEntrega { get; set; }
        public string formaVenta { get; set; }
        public string GrupoProducto { get; set; }
        public string iva { get; set; }
        public Nullable<int> oferta { get; set; }
        public int picking { get; set; }
        public string SubgrupoProducto { get; set; }
        public string texto { get; set; }
        public Nullable<byte> tipoLinea { get; set; }
        public string usuario { get; set; }
        public bool vistoBueno { get; set; }
        public decimal precioTarifa { get; set; }
        public int? Albaran { get; set; }
        public string Factura { get; set; }

        public override decimal SumaDescuentos
        {
            get
            {
                decimal descuentoPP = Pedido == null ? 0 : Pedido.DescuentoPP;
                return AplicarDescuento ? 1 - ((1 - DescuentoEntidad) * (1 - DescuentoProducto) * (1 - DescuentoLinea) * (1 - descuentoPP)) : 1 - ((1 - DescuentoLinea) * (1 - descuentoPP));
            }
        }
    }

}