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
    
    public partial class AgenciaTransporte
    {
        public AgenciaTransporte()
        {
            this.EnviosAgencias = new HashSet<EnviosAgencia>();
            this.EnviosAgencias1 = new HashSet<EnviosAgencia>();
        }
    
        public int Numero { get; set; }
        public string Empresa { get; set; }
        public string Nombre { get; set; }
        public string Ruta { get; set; }
        public string Identificador { get; set; }
        public string PrefijoCodigoBarras { get; set; }
        public string CuentaReembolsos { get; set; }
        public string Usuario { get; set; }
        public System.DateTime FechaModificacion { get; set; }
    
        public virtual Empresa Empresa1 { get; set; }
        public virtual Ruta Ruta1 { get; set; }
        public virtual ICollection<EnviosAgencia> EnviosAgencias { get; set; }
        public virtual ICollection<EnviosAgencia> EnviosAgencias1 { get; set; }
    }
}
