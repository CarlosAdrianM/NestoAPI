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
    
    public partial class ExtractoCliente
    {
        public string Empresa { get; set; }
        public int Nº_Orden { get; set; }
        public int Asiento { get; set; }
        public string Número { get; set; }
        public string Contacto { get; set; }
        public System.DateTime Fecha { get; set; }
        public string TipoApunte { get; set; }
        public string Nº_Documento { get; set; }
        public string Efecto { get; set; }
        public string Concepto { get; set; }
        public decimal Importe { get; set; }
        public decimal ImportePdte { get; set; }
        public string Delegación { get; set; }
        public string FormaVenta { get; set; }
        public string Vendedor { get; set; }
        public Nullable<System.DateTime> FechaVto { get; set; }
        public Nullable<int> Liquidado { get; set; }
        public string FormaPago { get; set; }
        public Nullable<int> Remesa { get; set; }
        public string CIF_NIF { get; set; }
        public string CCC { get; set; }
        public string Ruta { get; set; }
        public string Origen { get; set; }
        public string Estado { get; set; }
        public string Usuario { get; set; }
        public System.DateTime Fecha_Modificación { get; set; }
    
        public virtual Cliente Cliente { get; set; }
        public virtual Empresa Empresa1 { get; set; }
        public virtual Empresa Empresa2 { get; set; }
        public virtual FormaPago FormasPago { get; set; }
        public virtual FormaVenta FormasVenta { get; set; }
        public virtual CCC CCC1 { get; set; }
        public virtual Ruta Ruta1 { get; set; }
    }
}