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
    
    public partial class Proveedor
    {
        public Proveedor()
        {
            this.DescuentosProductoes = new HashSet<DescuentosProducto>();
            this.Empresas = new HashSet<Empresa>();
            this.ExtractoProductoes = new HashSet<ExtractoProducto>();
            this.ExtractoProveedors = new HashSet<ExtractoProveedor>();
            this.PersonasContactoProveedors = new HashSet<PersonaContactoProveedor>();
            this.CabFacturaCmps = new HashSet<CabFacturaCmp>();
            this.ProveedoresProductoes = new HashSet<ProveedoresProducto>();
            this.PedidosEspeciales = new HashSet<PedidosEspeciale>();
            this.LinPedidoCmps = new HashSet<LinPedidoCmp>();
            this.PreExtrProductoes = new HashSet<PreExtrProducto>();
            this.CabPedidoCmps = new HashSet<CabPedidoCmp>();
            this.OfertasProveedores = new HashSet<OfertaProveedor>();
        }
    
        public string Empresa { get; set; }
        public string Número { get; set; }
        public string Contacto { get; set; }
        public bool ProveedorPrincipal { get; set; }
        public string Nombre { get; set; }
        public string Dirección { get; set; }
        public string Población { get; set; }
        public string Teléfono { get; set; }
        public string Fax { get; set; }
        public string Comentarios { get; set; }
        public string CodPostal { get; set; }
        public string CIF_NIF { get; set; }
        public string Provincia { get; set; }
        public Nullable<short> Estado { get; set; }
        public string IVA { get; set; }
        public string Grupo { get; set; }
        public string PeriodoFacturación { get; set; }
        public string CCC { get; set; }
        public string Web { get; set; }
        public byte DíasEnServir { get; set; }
        public bool PedidoValorado { get; set; }
        public decimal C__IRPF { get; set; }
        public decimal ImporteMínimoPedido { get; set; }
        public bool ControlPendientes { get; set; }
        public string Usuario { get; set; }
        public System.DateTime Fecha_Modificación { get; set; }
    
        public virtual CCCProveedor CCCProveedore { get; set; }
        public virtual ICollection<DescuentosProducto> DescuentosProductoes { get; set; }
        public virtual ICollection<Empresa> Empresas { get; set; }
        public virtual Empresa Empresa1 { get; set; }
        public virtual ICollection<ExtractoProducto> ExtractoProductoes { get; set; }
        public virtual ICollection<ExtractoProveedor> ExtractoProveedors { get; set; }
        public virtual ICollection<PersonaContactoProveedor> PersonasContactoProveedors { get; set; }
        public virtual ICollection<CabFacturaCmp> CabFacturaCmps { get; set; }
        public virtual ICollection<ProveedoresProducto> ProveedoresProductoes { get; set; }
        public virtual ICollection<PedidosEspeciale> PedidosEspeciales { get; set; }
        public virtual ICollection<LinPedidoCmp> LinPedidoCmps { get; set; }
        public virtual ICollection<PreExtrProducto> PreExtrProductoes { get; set; }
        public virtual CodigoPostal CódigosPostales { get; set; }
        public virtual ICollection<CabPedidoCmp> CabPedidoCmps { get; set; }
        public virtual ICollection<OfertaProveedor> OfertasProveedores { get; set; }
        public virtual DatoConfirming DatosConfirming { get; set; }
    }
}
