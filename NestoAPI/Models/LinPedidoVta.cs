//------------------------------------------------------------------------------
// <auto-generated>
//     Este código se generó a partir de una plantilla.
//
//     Los cambios manuales en este archivo pueden causar un comportamiento inesperado de la aplicación.
//     Los cambios manuales en este archivo se sobrescribirán si se regenera el código.
// </auto-generated>
//------------------------------------------------------------------------------

namespace NestoAPI.Models
{
    using System;
    using System.Collections.Generic;
    
    public partial class LinPedidoVta
    {
        public LinPedidoVta()
        {
            this.PedidosEspeciales = new HashSet<PedidosEspeciale>();
            this.VendedorLinPedidoVtas = new HashSet<VendedorLinPedidoVta>();
        }
    
        public string Empresa { get; set; }
        public int Número { get; set; }
        public int Nº_Orden { get; set; }
        public string Nº_Cliente { get; set; }
        public string Contacto { get; set; }
        public Nullable<byte> TipoLinea { get; set; }
        public string Producto { get; set; }
        public string Almacén { get; set; }
        public System.DateTime Fecha_Entrega { get; set; }
        public string Texto { get; set; }
        public Nullable<short> Cantidad { get; set; }
        public Nullable<decimal> Precio { get; set; }
        public string IVA { get; set; }
        public byte PorcentajeIVA { get; set; }
        public decimal PorcentajeRE { get; set; }
        public decimal Bruto { get; set; }
        public decimal DescuentoCliente { get; set; }
        public decimal DescuentoProducto { get; set; }
        public decimal Descuento { get; set; }
        public decimal DescuentoPP { get; set; }
        public decimal SumaDescuentos { get; set; }
        public decimal ImporteDto { get; set; }
        public decimal Base_Imponible { get; set; }
        public decimal ImporteIVA { get; set; }
        public decimal ImporteRE { get; set; }
        public decimal Total { get; set; }
        public bool Aplicar_Dto { get; set; }
        public string Delegación { get; set; }
        public string Forma_Venta { get; set; }
        public Nullable<int> Nº_Albarán { get; set; }
        public Nullable<System.DateTime> Fecha_Albarán { get; set; }
        public string Nº_Factura { get; set; }
        public Nullable<System.DateTime> Fecha_Factura { get; set; }
        public Nullable<int> Picking { get; set; }
        public short Estado { get; set; }
        public string Grupo { get; set; }
        public string SubGrupo { get; set; }
        public Nullable<int> NºOferta { get; set; }
        public bool GeneraBonificación { get; set; }
        public string Familia { get; set; }
        public string TipoExclusiva { get; set; }
        public bool YaFacturado { get; set; }
        public bool VtoBueno { get; set; }
        public decimal Coste { get; set; }
        public string BlancoParaBorrar { get; set; }
        public bool Reponer { get; set; }
        public Nullable<decimal> PrecioTarifa { get; set; }
        public int Recoger { get; set; }
        public bool LineaParcial { get; set; }
        public Nullable<int> EstadoProducto { get; set; }
        public string CentroCoste { get; set; }
        public string Departamento { get; set; }
        public string NumSerie { get; set; }
        public string Usuario { get; set; }
        public System.DateTime Fecha_Modificación { get; set; }
        public byte[] RowVersion { get; set; }
    
        public virtual CabPedidoVta CabPedidoVta { get; set; }
        public virtual Empresa Empresa1 { get; set; }
        public virtual Cliente Cliente { get; set; }
        public virtual Familia Familia1 { get; set; }
        public virtual SubGruposProducto SubGruposProducto { get; set; }
        public virtual FormaVenta FormasVenta { get; set; }
        public virtual ICollection<PedidosEspeciale> PedidosEspeciales { get; set; }
        public virtual CabFacturaVta CabFacturaVta { get; set; }
        public virtual ICollection<VendedorLinPedidoVta> VendedorLinPedidoVtas { get; set; }
    }
}
