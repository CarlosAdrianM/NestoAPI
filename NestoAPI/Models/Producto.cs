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
    
    public partial class Producto
    {
        public Producto()
        {
            this.Productos1 = new HashSet<Producto>();
            this.DescuentosProductoes = new HashSet<DescuentosProducto>();
            this.ExtractoProductoes = new HashSet<ExtractoProducto>();
            this.InventariosCuadres = new HashSet<InventarioCuadre>();
            this.Inventarios = new HashSet<Inventario>();
            this.ProveedoresProductoes = new HashSet<ProveedoresProducto>();
            this.OfertasPermitidas = new HashSet<OfertaPermitida>();
            this.OfertasCombinadasDetalles = new HashSet<OfertaCombinadaDetalle>();
            this.PreExtrProductoes = new HashSet<PreExtrProducto>();
            this.RegaloImportePedidoes = new HashSet<RegaloImportePedido>();
            this.OfertasProveedores = new HashSet<OfertaProveedor>();
            this.Kits = new HashSet<Kit>();
            this.Kits1 = new HashSet<Kit>();
            this.GruposProductoes = new HashSet<GruposProducto>();
        }
    
        public string Empresa { get; set; }
        public string Número { get; set; }
        public string Nombre { get; set; }
        public string Grupo { get; set; }
        public Nullable<decimal> PVP { get; set; }
        public string IVA_Soportado { get; set; }
        public string IVA_Repercutido { get; set; }
        public string Comentarios { get; set; }
        public Nullable<short> Estado { get; set; }
        public string CodBarras { get; set; }
        public bool Aplicar_Dto { get; set; }
        public string SubGrupo { get; set; }
        public Nullable<short> Tamaño { get; set; }
        public string UnidadMedida { get; set; }
        public string Familia { get; set; }
        public string Foto { get; set; }
        public Nullable<decimal> PrecioMedio { get; set; }
        public bool Ficticio { get; set; }
        public Nullable<System.DateTime> FechaInicio { get; set; }
        public Nullable<System.DateTime> FechaFinal { get; set; }
        public Nullable<byte> NumeroSesiones { get; set; }
        public bool MateriaPrima { get; set; }
        public short UnidadesPorEtiqueta { get; set; }
        public bool VariasOpciones { get; set; }
        public bool Ubicar { get; set; }
        public string ProductoAnterior { get; set; }
        public bool GestionStockManual { get; set; }
        public bool RoturaStockProveedor { get; set; }
        public int ProductosPorCaja { get; set; }
        public int CantidadMinimaPedido { get; set; }
        public bool Revisado { get; set; }
        public string ComentariosFactura { get; set; }
        public bool NecesitaNumSerie { get; set; }
        public string Usuario { get; set; }
        public System.DateTime Fecha_Modificación { get; set; }
    
        public virtual Empresa Empresa1 { get; set; }
        public virtual ICollection<Producto> Productos1 { get; set; }
        public virtual Producto Producto1 { get; set; }
        public virtual Familia Familia1 { get; set; }
        public virtual SubGruposProducto SubGruposProducto { get; set; }
        public virtual ICollection<DescuentosProducto> DescuentosProductoes { get; set; }
        public virtual ICollection<ExtractoProducto> ExtractoProductoes { get; set; }
        public virtual ICollection<InventarioCuadre> InventariosCuadres { get; set; }
        public virtual ICollection<Inventario> Inventarios { get; set; }
        public virtual ICollection<ProveedoresProducto> ProveedoresProductoes { get; set; }
        public virtual ICollection<OfertaPermitida> OfertasPermitidas { get; set; }
        public virtual ICollection<OfertaCombinadaDetalle> OfertasCombinadasDetalles { get; set; }
        public virtual ICollection<PreExtrProducto> PreExtrProductoes { get; set; }
        public virtual ICollection<RegaloImportePedido> RegaloImportePedidoes { get; set; }
        public virtual ClasificacionMasVendido ClasificacionMasVendido { get; set; }
        public virtual ICollection<OfertaProveedor> OfertasProveedores { get; set; }
        public virtual ICollection<Kit> Kits { get; set; }
        public virtual ICollection<Kit> Kits1 { get; set; }
        public virtual ICollection<GruposProducto> GruposProductoes { get; set; }
        public virtual GruposProducto GruposProducto { get; set; }
    }
}
