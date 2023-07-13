using NestoAPI.Models.PedidosBase;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.PedidosCompra
{
    public class LineaPedidoCompraDTO : LineaPedidoBase
    {
        public LineaPedidoCompraDTO() : base()
        {
            Descuentos = new List<DescuentoCantidadCompra>();
        }
        public int Id { get; set; }
        public int Estado { get; set; }
        public string TipoLinea { get; set; }
        //public string Producto { get; set; }
        public string Texto { get; set; }
        public DateTime FechaRecepcion { get; set; }
        private int _cantidad;
        public override int Cantidad { 
            get => _cantidad;
            set {
                if (Descuentos != null && Descuentos.Any())
                {
                    var descuentoActual = Descuentos.Where(d => d.CantidadMinima <= _cantidad).OrderBy(d => d.CantidadMinima).FirstOrDefault();
                    var descuentoNuevo = Descuentos.Where(d => d.CantidadMinima <= value).OrderBy(d => d.CantidadMinima).FirstOrDefault();

                    if (descuentoNuevo == null)
                    {
                        descuentoNuevo = new DescuentoCantidadCompra
                        {
                            CantidadMinima = 0,
                            Descuento = 0,
                            Precio = 0
                        };
                    }

                    if (PrecioUnitario != descuentoNuevo.Precio)
                    {
                        PrecioUnitario = descuentoNuevo.Precio;
                    }
                    if (DescuentoProducto != descuentoNuevo.Descuento)
                    {
                        DescuentoProducto = descuentoNuevo.Descuento;
                    }
                }
                _cantidad = value;
            }
        }
        public int CantidadBruta { get; set; }
        public decimal DescuentoProveedor { 
            get => DescuentoEntidad; 
            set => DescuentoEntidad = value; 
        }
        //public decimal PorcentajeIva { get; set; }
        public string CodigoIvaProducto { get; set; }
        public int StockMaximo { get; set; }
        public int PendienteEntregar { get; set; }
        public int PendienteRecibir { get; set; }
        public int Stock { get; set; }
        public int Multiplos { get; set; }
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
        public decimal PrecioTarifa { get; set; }
        public int EstadoProducto { get; set; }



        public override decimal Bruto { get => (decimal)(CantidadCobrada == null ? Cantidad * PrecioUnitario : CantidadCobrada * PrecioUnitario); }
        //public decimal SumaDescuentos { get => AplicarDescuentos ? 1 - (1 - DescuentoProveedor) * (1 - DescuentoProducto) * (1 - DescuentoLinea) : 0; }
        //public decimal ImporteDescuento { get => Math.Round(Bruto * SumaDescuentos, 2, MidpointRounding.AwayFromZero); }
        //public override decimal BaseImponible { get => Math.Round(Bruto - ImporteDescuento, 2, MidpointRounding.AwayFromZero); }
        //public decimal ImporteIva { get => BaseImponible * PorcentajeIva; }
        //public decimal Total { get => BaseImponible + ImporteIva; }
        public int? CantidadCobrada { get; set; }
        public int? CantidadRegalo { get; set; }


        public List<DescuentoCantidadCompra> Descuentos { get; set; }
        public List<OfertaCompra> Ofertas { get; set; }
    }
}