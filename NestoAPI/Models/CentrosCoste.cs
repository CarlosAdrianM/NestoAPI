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
    
    public partial class CentrosCoste
    {
        public CentrosCoste()
        {
            this.ExtractoProductoes = new HashSet<ExtractoProducto>();
            this.Inmovilizados = new HashSet<Inmovilizado>();
        }
    
        public string Empresa { get; set; }
        public string Número { get; set; }
        public string Descripción { get; set; }
        public string Delegación { get; set; }
        public string Departamento { get; set; }
        public string Usuario { get; set; }
        public short Estado { get; set; }
        public byte[] FechaModificación { get; set; }
        public System.DateTime FechaCreación { get; set; }
    
        public virtual ICollection<ExtractoProducto> ExtractoProductoes { get; set; }
        public virtual ICollection<Inmovilizado> Inmovilizados { get; set; }
    }
}