using NestoAPI.Infraestructure;

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
        //
        // NestoAPI#453: el redondeo tiene que ser EXACTAMENTE el de
        // GestorPedidosVenta.CalcularImportesLinea, que es lo que se graba en LinPedidoVta y lo
        // que exige el asiento contable del SP prdCrearFacturaVta (issues #242/#243):
        //   ImporteDto    = ROUND(Bruto * SumaDescuentos, 2)   <- el descuento se redondea ANTES
        //   BaseImponible = ROUND(Bruto, 2) - ImporteDto
        //
        // Antes esto calculaba ROUND(Bruto - Bruto * SumaDescuentos), que da un céntimo distinto
        // en cuanto Bruto * Dto tiene más de dos decimales: 63,50 con un 15 % daba 53,98 aquí y
        // 53,97 en la base de datos. Ese céntimo se colaba en el total del DTO y de ahí en el
        // correo del pedido, en CuadrarEfectos (que "corregía" los vencimientos buenos hasta
        // descuadrarlos) y en la proforma, que se negaba a generarse porque los vencimientos no
        // cuadraban con el total.
        public decimal BaseImponible => RoundingHelper.DosDecimalesRound(Bruto) - ImporteDescuento;
        public virtual decimal Bruto => PrecioUnitario * Cantidad;
        public decimal ImporteDescuento => RoundingHelper.DosDecimalesRound(Bruto * SumaDescuentos);
        public virtual decimal ImporteIva => BaseImponible * PorcentajeIva;
        public virtual decimal ImporteRecargoEquivalencia => BaseImponible * PorcentajeRecargoEquivalencia;
        public virtual decimal SumaDescuentos => AplicarDescuento ? 1 - ((1 - DescuentoEntidad) * (1 - DescuentoProducto) * (1 - DescuentoLinea)) : DescuentoLinea;
        public virtual decimal Total => BaseImponible + ImporteIva + ImporteRecargoEquivalencia;
    }
}