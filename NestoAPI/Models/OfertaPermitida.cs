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
    
    public partial class OfertaPermitida
    {
        public string Empresa { get; set; }
        public int NºOrden { get; set; }
        public string Número { get; set; }
        public string Familia { get; set; }
        public short CantidadConPrecio { get; set; }
        public short CantidadRegalo { get; set; }
        public bool Denegar { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Usuario { get; set; }
        public System.DateTime FechaModificación { get; set; }
        public string FiltroProducto { get; set; }
    
        public virtual Cliente Cliente1 { get; set; }
        public virtual Empresa Empresa1 { get; set; }
        public virtual Producto Producto { get; set; }
    }
}
