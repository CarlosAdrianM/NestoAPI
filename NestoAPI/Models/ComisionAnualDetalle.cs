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
    
    public partial class ComisionAnualDetalle
    {
        public int Id { get; set; }
        public short EstadoFamilia { get; set; }
        public decimal BaseImponible { get; set; }
        public string Vendedor { get; set; }
        public string Etiqueta { get; set; }
        public short Anno { get; set; }
        public byte Mes { get; set; }
        public int Pedido { get; set; }
        public string Usuario { get; set; }
        public System.DateTime FechaModificacion { get; set; }
    }
}
