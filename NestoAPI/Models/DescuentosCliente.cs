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
    
    public partial class DescuentosCliente
    {
        public string Empresa { get; set; }
        public string Nº_Cliente { get; set; }
        public string Contacto { get; set; }
        public decimal ImporteMínimo { get; set; }
        public decimal Descuento { get; set; }
        public string Usuario { get; set; }
        public System.DateTime Fecha_Modificación { get; set; }
    
        public virtual Cliente Cliente { get; set; }
        public virtual Empresa Empresa1 { get; set; }
    }
}
