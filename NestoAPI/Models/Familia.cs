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
    
    public partial class Familia
    {
        public Familia()
        {
            this.LinPedidoVtas = new HashSet<LinPedidoVta>();
            this.Productos = new HashSet<Producto>();
            this.DescuentosProductoes = new HashSet<DescuentosProducto>();
            this.Inventarios = new HashSet<Inventario>();
        }
    
        public string Empresa { get; set; }
        public string Número { get; set; }
        public string Descripción { get; set; }
        public string TipoExclusiva { get; set; }
        public short Estado { get; set; }
        public decimal C_DtoMáximoComisión { get; set; }
        public decimal C_ComisiónFija { get; set; }
        public string Usuario { get; set; }
        public System.DateTime Fecha_Modificación { get; set; }
    
        public virtual Empresa Empresa1 { get; set; }
        public virtual ICollection<LinPedidoVta> LinPedidoVtas { get; set; }
        public virtual ICollection<Producto> Productos { get; set; }
        public virtual ICollection<DescuentosProducto> DescuentosProductoes { get; set; }
        public virtual ICollection<Inventario> Inventarios { get; set; }
    }
}
