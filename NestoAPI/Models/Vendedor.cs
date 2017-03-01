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
    
    public partial class Vendedor
    {
        public Vendedor()
        {
            this.Clientes = new HashSet<Cliente>();
            this.Empresas = new HashSet<Empresa>();
            this.EnviosAgencias = new HashSet<EnviosAgencia>();
            this.ExtractoClientes = new HashSet<ExtractoCliente>();
            this.ExtractoProductoes = new HashSet<ExtractoProducto>();
            this.Vendedores1 = new HashSet<Vendedor>();
            this.VendedoresClienteGrupoProductoes = new HashSet<VendedorClienteGrupoProducto>();
            this.VendedoresPedidoGrupoProductoes = new HashSet<VendedorPedidoGrupoProducto>();
        }
    
        public string Empresa { get; set; }
        public string Número { get; set; }
        public string Descripción { get; set; }
        public short Estado { get; set; }
        public string VendedorAnterior { get; set; }
        public string TipoComisión { get; set; }
        public string Mail { get; set; }
        public string Usuario { get; set; }
        public System.DateTime Fecha_Modificación { get; set; }
    
        public virtual ICollection<Cliente> Clientes { get; set; }
        public virtual ICollection<Empresa> Empresas { get; set; }
        public virtual Empresa Empresa1 { get; set; }
        public virtual ICollection<EnviosAgencia> EnviosAgencias { get; set; }
        public virtual ICollection<ExtractoCliente> ExtractoClientes { get; set; }
        public virtual ICollection<ExtractoProducto> ExtractoProductoes { get; set; }
        public virtual ICollection<Vendedor> Vendedores1 { get; set; }
        public virtual Vendedor Vendedore1 { get; set; }
        public virtual ICollection<VendedorClienteGrupoProducto> VendedoresClienteGrupoProductoes { get; set; }
        public virtual ICollection<VendedorPedidoGrupoProducto> VendedoresPedidoGrupoProductoes { get; set; }
    }
}
