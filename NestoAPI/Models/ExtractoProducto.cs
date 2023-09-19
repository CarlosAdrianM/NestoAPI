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
    
    public partial class ExtractoProducto
    {
        public string Empresa { get; set; }
        public int Nº_Orden { get; set; }
        public string Número { get; set; }
        public System.DateTime Fecha { get; set; }
        public string Nº_Cliente { get; set; }
        public string ContactoCliente { get; set; }
        public string NºProveedor { get; set; }
        public string ContactoProveedor { get; set; }
        public Nullable<int> Albarán { get; set; }
        public string Factura { get; set; }
        public string Texto { get; set; }
        public string Almacén { get; set; }
        public string Grupo { get; set; }
        public short Cantidad { get; set; }
        public Nullable<decimal> Coste { get; set; }
        public Nullable<decimal> Importe { get; set; }
        public string Delegación { get; set; }
        public string Forma_Venta { get; set; }
        public bool Asiento_Automático { get; set; }
        public Nullable<int> LinPedido { get; set; }
        public string Vendedor { get; set; }
        public Nullable<int> NºTraspaso { get; set; }
        public Nullable<int> NºPedido { get; set; }
        public string Diario { get; set; }
        public string CentroCoste { get; set; }
        public string Departamento { get; set; }
        public string Usuario { get; set; }
        public System.DateTime Fecha_Modificación { get; set; }
    
        public virtual Cliente Cliente { get; set; }
        public virtual Empresa Empresa1 { get; set; }
        public virtual Producto Producto { get; set; }
        public virtual Proveedor Proveedore { get; set; }
        public virtual FormaVenta FormasVenta { get; set; }
        public virtual CentrosCoste CentrosCoste { get; set; }
        public virtual Vendedor Vendedore { get; set; }
        public virtual DiarioProducto DiariosProducto { get; set; }
    }
}
