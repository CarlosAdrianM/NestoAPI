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
    
    public partial class Inventario
    {
        public int NºOrden { get; set; }
        public string Empresa { get; set; }
        public string Almacén { get; set; }
        public System.DateTime Fecha { get; set; }
        public string Número { get; set; }
        public string Descripción { get; set; }
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
        public string Familia { get; set; }
        public Nullable<int> StockCalculado { get; set; }
        public Nullable<int> StockReal { get; set; }
        public byte Estado { get; set; }
        public decimal Valor { get; set; }
        public Nullable<int> NºTraspaso { get; set; }
        public string Aplicacion { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public string Usuario { get; set; }
        public System.DateTime Fecha_Modificación { get; set; }
    
        public virtual Empresa Empresa1 { get; set; }
        public virtual Familia Familia1 { get; set; }
        public virtual Producto Producto { get; set; }
        public virtual SubGruposProducto SubGruposProducto { get; set; }
    }
}