﻿//------------------------------------------------------------------------------
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
    using System.Data.Entity;
    using System.Data.Entity.Infrastructure;
    using System.Data.Entity.Core.Objects;
    using System.Linq;
    
    public partial class NVEntities : DbContext
    {
        public NVEntities()
            : base("name=NVEntities")
        {
        }
    
        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            throw new UnintentionalCodeFirstException();
        }
    
        public virtual DbSet<EnviosAgencia> EnviosAgencias { get; set; }
        public virtual DbSet<Empresa> Empresas { get; set; }
        public virtual DbSet<CabPedidoVta> CabPedidoVtas { get; set; }
        public virtual DbSet<LinPedidoVta> LinPedidoVtas { get; set; }
        public virtual DbSet<Cliente> Clientes { get; set; }
        public virtual DbSet<Producto> Productos { get; set; }
        public virtual DbSet<Familia> Familias { get; set; }
        public virtual DbSet<SubGruposProducto> SubGruposProductoes { get; set; }
        public virtual DbSet<CondPagoCliente> CondPagoClientes { get; set; }
        public virtual DbSet<DescuentosCliente> DescuentosClientes { get; set; }
        public virtual DbSet<DescuentosProducto> DescuentosProductoes { get; set; }
        public virtual DbSet<DíasPagoClientes> DíasPagoClientes { get; set; }
        public virtual DbSet<ContadorGlobal> ContadoresGlobales { get; set; }
        public virtual DbSet<FormaPago> FormasPago { get; set; }
        public virtual DbSet<PlazoPago> PlazosPago { get; set; }
        public virtual DbSet<ParametroIVA> ParametrosIVA { get; set; }
        public virtual DbSet<ParametroUsuario> ParametrosUsuario { get; set; }
        public virtual DbSet<ExtractoProducto> ExtractosProducto { get; set; }
        public virtual DbSet<CabRemesaPago> CabRemesasPago { get; set; }
        public virtual DbSet<ExtractoProveedor> ExtractosProveedor { get; set; }
        public virtual DbSet<Banco> Bancos { get; set; }
        public virtual DbSet<CCCProveedor> CCCProveedores { get; set; }
        public virtual DbSet<PersonaContactoProveedor> PersonasContactoProveedores { get; set; }
        public virtual DbSet<Proveedor> Proveedores { get; set; }
        public virtual DbSet<CabFacturaCmp> CabFacturasCmp { get; set; }
        public virtual DbSet<FormaVenta> FormasVenta { get; set; }
        public virtual DbSet<InventarioCuadre> InventarioCuadres { get; set; }
        public virtual DbSet<Inventario> Inventarios { get; set; }
        public virtual DbSet<ExtractoCliente> ExtractosCliente { get; set; }
    
        public virtual int prdAjustarDíasPagoCliente(string empresa, string cliente, string contacto, Nullable<System.DateTime> fechaIn, ObjectParameter fechaOut)
        {
            var empresaParameter = empresa != null ?
                new ObjectParameter("Empresa", empresa) :
                new ObjectParameter("Empresa", typeof(string));
    
            var clienteParameter = cliente != null ?
                new ObjectParameter("Cliente", cliente) :
                new ObjectParameter("Cliente", typeof(string));
    
            var contactoParameter = contacto != null ?
                new ObjectParameter("Contacto", contacto) :
                new ObjectParameter("Contacto", typeof(string));
    
            var fechaInParameter = fechaIn.HasValue ?
                new ObjectParameter("FechaIn", fechaIn) :
                new ObjectParameter("FechaIn", typeof(System.DateTime));
    
            return ((IObjectContextAdapter)this).ObjectContext.ExecuteFunction("prdAjustarDíasPagoCliente", empresaParameter, clienteParameter, contactoParameter, fechaInParameter, fechaOut);
        }
    }
}
