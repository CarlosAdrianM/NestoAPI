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
    
    public partial class ParametroIVA
    {
        public string Empresa { get; set; }
        public string IVA_Producto { get; set; }
        public string IVA_Cliente_Prov { get; set; }
        public Nullable<decimal> C__IVA { get; set; }
        public Nullable<decimal> C__RE { get; set; }
        public string CtaRepercutido { get; set; }
        public string CtaSoportado { get; set; }
        public string CtaRecargoRepercutido { get; set; }
        public string CtaRecargoSoportado { get; set; }
        public string Usuario { get; set; }
        public System.DateTime Fecha_Modificación { get; set; }
    
        public virtual Empresa Empresa1 { get; set; }
    }
}
