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
    
    public partial class CCCProveedor
    {
        public CCCProveedor()
        {
            this.Proveedores = new HashSet<Proveedor>();
        }
    
        public string Empresa { get; set; }
        public string NºProveedor { get; set; }
        public string Contacto { get; set; }
        public string Número { get; set; }
        public string Entidad { get; set; }
        public string Oficina { get; set; }
        public string DC { get; set; }
        public string Nº_Cuenta { get; set; }
        public short Estado { get; set; }
        public string Usuario { get; set; }
        public System.DateTime Fecha_Modificación { get; set; }
        public string IbanNoResidente { get; set; }
        public string Swift { get; set; }
    
        public virtual ICollection<Proveedor> Proveedores { get; set; }
    }
}
