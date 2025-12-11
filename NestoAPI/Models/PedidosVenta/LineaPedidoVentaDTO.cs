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

        // Carlos 09/12/25: Issue #253/#52 - Indica si el producto es ficticio (cuenta contable, etc.)
        // Se usa para determinar si se puede cambiar el almacén de la línea
        public bool EsFicticio { get; set; }

        // Carlos 23/10/25: para controlar en modificaciones qué líneas son nuevas o tienen cantidad modificada
        [JsonIgnore]
        public int? CantidadAnterior { get; set; }
        [JsonIgnore]
        public string ProductoAnterior { get; set; }
        [JsonIgnore]
        public bool EsLineaNueva => id == 0;
        [JsonIgnore]
        public bool CambioProducto => !EsLineaNueva && ProductoAnterior != null && ProductoAnterior.Trim() != Producto?.Trim();

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