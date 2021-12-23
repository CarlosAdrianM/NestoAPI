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
    
    public partial class LinPedidoCmp
    {
        public string Empresa { get; set; }
        public int Número { get; set; }
        public int NºOrden { get; set; }
        public string NºProveedor { get; set; }
        public string Contacto { get; set; }
        public string TipoLínea { get; set; }
        public string Producto { get; set; }
        public string Almacén { get; set; }
        public Nullable<System.DateTime> FechaRecepción { get; set; }
        public string Texto { get; set; }
        public Nullable<short> Cantidad { get; set; }
        public decimal Precio { get; set; }
        public decimal Bruto { get; set; }
        public string IVA { get; set; }
        public decimal PorcentajeIVA { get; set; }
        public decimal PorcentajeRE { get; set; }
        public decimal DescuentoProveedor { get; set; }
        public decimal DescuentoProducto { get; set; }
        public decimal Descuento { get; set; }
        public decimal DescuentoPP { get; set; }
        public decimal SumaDescuentos { get; set; }
        public decimal ImporteDto { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal ImporteIVA { get; set; }
        public decimal ImporteRE { get; set; }
        public decimal Total { get; set; }
        public bool AplicarDto { get; set; }
        public string Delegación { get; set; }
        public string FormaVenta { get; set; }
        public Nullable<int> NºAlbarán { get; set; }
        public Nullable<System.DateTime> FechaAlbarán { get; set; }
        public string NºFactura { get; set; }
        public Nullable<System.DateTime> FechaFactura { get; set; }
        public short Estado { get; set; }
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
        public Nullable<byte> NºOferta { get; set; }
        public bool VistoBueno { get; set; }
        public bool Enviado { get; set; }
        public decimal Coste { get; set; }
        public string CentroCoste { get; set; }
        public Nullable<decimal> PrecioTarifa { get; set; }
        public bool YaFacturado { get; set; }
        public bool Reponer { get; set; }
        public string Departamento { get; set; }
        public decimal C__IRPF { get; set; }
        public Nullable<int> EstadoProducto { get; set; }
        public string NumSerie { get; set; }
        public string Usuario { get; set; }
        public System.DateTime Fecha_Modificación { get; set; }
    
        public virtual CabFacturaCmp CabFacturaCmp { get; set; }
        public virtual CentrosCoste CentrosCoste { get; set; }
        public virtual Empresa Empresa1 { get; set; }
        public virtual FormaVenta FormasVenta { get; set; }
        public virtual Proveedor Proveedore { get; set; }
        public virtual CabPedidoCmp CabPedidoCmp { get; set; }
    }
}
