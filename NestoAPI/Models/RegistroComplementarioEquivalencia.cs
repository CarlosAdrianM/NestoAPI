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
    
    public partial class RegistroComplementarioEquivalencia
    {
        public int Id { get; set; }
        public int ApuntesBancariosId { get; set; }
        public string CodigoDato { get; set; }
        public string ClaveDivisaOrigen { get; set; }
        public decimal ImporteEquivalencia { get; set; }
        public string Usuario { get; set; }
        public System.DateTime FechaCreacion { get; set; }
    
        public virtual ApunteBancario ApuntesBancario { get; set; }
    }
}
