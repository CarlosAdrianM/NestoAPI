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
    
    public partial class PersonaContactoProveedor
    {
        public string Empresa { get; set; }
        public string NºProveedor { get; set; }
        public string Contacto { get; set; }
        public string Número { get; set; }
        public string Nombre { get; set; }
        public short Cargo { get; set; }
        public string Comentarios { get; set; }
        public string Fax { get; set; }
        public string Teléfono { get; set; }
        public string CorreoElectrónico { get; set; }
        public bool EnviarBoletin { get; set; }
        public short Estado { get; set; }
        public string Saludo { get; set; }
        public string Usuario { get; set; }
        public System.DateTime Fecha_Modificación { get; set; }
    
        public virtual Empresa Empresa1 { get; set; }
        public virtual Proveedor Proveedore { get; set; }
    }
}
