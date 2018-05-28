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
    
    public partial class FormaVenta
    {
        public FormaVenta()
        {
            this.Empresas = new HashSet<Empresa>();
            this.ExtractoProductoes = new HashSet<ExtractoProducto>();
            this.ExtractoProveedors = new HashSet<ExtractoProveedor>();
            this.LinPedidoVtas = new HashSet<LinPedidoVta>();
            this.ExtractoClientes = new HashSet<ExtractoCliente>();
            this.LinPedidoCmps = new HashSet<LinPedidoCmp>();
            this.PreExtrProductoes = new HashSet<PreExtrProducto>();
        }
    
        public string Empresa { get; set; }
        public string Número { get; set; }
        public string Descripción { get; set; }
        public bool VisiblePorComerciales { get; set; }
        public string Usuario { get; set; }
        public System.DateTime Fecha_Modificación { get; set; }
    
        public virtual ICollection<Empresa> Empresas { get; set; }
        public virtual Empresa Empresa1 { get; set; }
        public virtual ICollection<ExtractoProducto> ExtractoProductoes { get; set; }
        public virtual ICollection<ExtractoProveedor> ExtractoProveedors { get; set; }
        public virtual ICollection<LinPedidoVta> LinPedidoVtas { get; set; }
        public virtual ICollection<ExtractoCliente> ExtractoClientes { get; set; }
        public virtual ICollection<LinPedidoCmp> LinPedidoCmps { get; set; }
        public virtual ICollection<PreExtrProducto> PreExtrProductoes { get; set; }
    }
}
