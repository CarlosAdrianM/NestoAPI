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
    
    public partial class InventarioCuadre
    {
        public int Numero { get; set; }
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public System.DateTime Fecha { get; set; }
        public short Cantidad { get; set; }
        public byte Estado { get; set; }
        public string Usuario { get; set; }
        public System.DateTime FechaModificacion { get; set; }
    
        public virtual Empresa Empresa1 { get; set; }
        public virtual Producto Producto1 { get; set; }
    }
}
