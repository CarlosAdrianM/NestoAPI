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
    
    public partial class VendedorClienteGrupoProducto
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string GrupoProducto { get; set; }
        public string Vendedor { get; set; }
        public string Usuario { get; set; }
        public System.DateTime FechaModificacion { get; set; }
    
        public virtual Cliente Cliente1 { get; set; }
        public virtual Vendedor Vendedore { get; set; }
    }
}
