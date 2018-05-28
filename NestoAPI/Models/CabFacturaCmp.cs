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
    
    public partial class CabFacturaCmp
    {
        public CabFacturaCmp()
        {
            this.LinPedidoCmps = new HashSet<LinPedidoCmp>();
        }
    
        public string Empresa { get; set; }
        public string Número { get; set; }
        public System.DateTime Fecha { get; set; }
        public string NºProveedor { get; set; }
        public string Contacto { get; set; }
        public string FormaPago { get; set; }
        public string PlazosPago { get; set; }
        public string IVA { get; set; }
        public Nullable<bool> Espejo { get; set; }
        public Nullable<System.DateTime> PrimerVencimiento { get; set; }
        public string NºDocumentoProv { get; set; }
        public string Origen { get; set; }
        public decimal C__IRPF { get; set; }
        public Nullable<System.DateTime> FechaProveedor { get; set; }
        public string Usuario { get; set; }
        public System.DateTime Fecha_Modificación { get; set; }
    
        public virtual Empresa Empresa1 { get; set; }
        public virtual Empresa Empresa2 { get; set; }
        public virtual FormaPago FormasPago { get; set; }
        public virtual PlazoPago PlazosPago1 { get; set; }
        public virtual Proveedor Proveedore { get; set; }
        public virtual ICollection<LinPedidoCmp> LinPedidoCmps { get; set; }
    }
}
