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
    
    public partial class OfertaProveedor
    {
        public string Empresa { get; set; }
        public int NºOrden { get; set; }
        public string NºProveedor { get; set; }
        public string Contacto { get; set; }
        public string Producto { get; set; }
        public int CantidadOferta { get; set; }
        public int CantidadRegalo { get; set; }
        public short GrupoOfertas { get; set; }
        public string Usuario { get; set; }
        public System.DateTime FechaCreacion { get; set; }
        public byte[] FechaModificacion { get; set; }
    
        public virtual Empresa Empresa1 { get; set; }
        public virtual Producto Producto1 { get; set; }
        public virtual Proveedor Proveedore { get; set; }
    }
}
