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
    
    public partial class FormaPago
    {
        public FormaPago()
        {
            this.CondPagoClientes = new HashSet<CondPagoCliente>();
            this.Empresas = new HashSet<Empresa>();
            this.Empresas1 = new HashSet<Empresa>();
            this.Empresas2 = new HashSet<Empresa>();
        }
    
        public string Empresa { get; set; }
        public string Número { get; set; }
        public string Descripción { get; set; }
        public bool BloquearPagos { get; set; }
        public bool Informe { get; set; }
        public bool CCCObligatorio { get; set; }
    
        public virtual ICollection<CondPagoCliente> CondPagoClientes { get; set; }
        public virtual ICollection<Empresa> Empresas { get; set; }
        public virtual ICollection<Empresa> Empresas1 { get; set; }
        public virtual ICollection<Empresa> Empresas2 { get; set; }
        public virtual Empresa Empresa1 { get; set; }
    }
}
