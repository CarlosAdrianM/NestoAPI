using NestoAPI.Models.PedidosCompra;
using System;
using System.Drawing;

namespace NestoAPI.Models.PedidosBase
{
    public class LineaPedidoBase
    {
        // Propiedades
        public bool AplicarDescuento { get; set; } = true;
        public virtual int Cantidad { get; set; } = 1;
        public decimal DescuentoEntidad { get; set; }
        public decimal DescuentoLinea { get; set; }
        public decimal DescuentoPP { get; set; }
        public decimal DescuentoProducto { get; set; }
        public decimal PorcentajeIva { get; set; }
        public decimal PorcentajeRecargoEquivalencia { get; set; }
        public decimal PrecioUnitario { get; set; }
        public string Producto { get; set; }

        // Propiedades calculadas
        public decimal BaseImponible { get => Math.Round(Bruto - ImporteDescuento, 2, MidpointRounding.AwayFromZero); }
        public virtual decimal Bruto { get => PrecioUnitario * Cantidad; }
        public decimal ImporteDescuento { get => Bruto * SumaDescuentos; }
        public virtual decimal ImporteIva { get => BaseImponible * PorcentajeIva; }
        public virtual decimal ImporteRecargoEquivalencia { get => BaseImponible * PorcentajeRecargoEquivalencia; }
        public virtual decimal SumaDescuentos { get => AplicarDescuento ? 1 - (1 - DescuentoEntidad) * (1 - DescuentoProducto) * (1 - DescuentoLinea) : DescuentoLinea; }
        public virtual decimal Total { get => BaseImponible + ImporteIva + ImporteRecargoEquivalencia; }
    }
}