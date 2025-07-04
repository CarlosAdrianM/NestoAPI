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
        public virtual DbSet<CCC> CCCs { get; set; }
        public virtual DbSet<ProveedoresProducto> ProveedoresProductoes { get; set; }
        public virtual DbSet<Modificacion> Modificaciones { get; set; }
        public virtual DbSet<Ruta> Rutas { get; set; }
        public virtual DbSet<Ubicacion> Ubicaciones { get; set; }
        public virtual DbSet<Inmovilizado> Inmovilizados { get; set; }
        public virtual DbSet<PlanCuenta> PlanCuentas { get; set; }
        public virtual DbSet<PedidosEspeciale> PedidosEspeciales { get; set; }
        public virtual DbSet<UsuarioVendedor> UsuarioVendedores { get; set; }
        public virtual DbSet<CentrosCoste> CentrosCostes { get; set; }
        public virtual DbSet<VendedorClienteGrupoProducto> VendedoresClientesGruposProductos { get; set; }
        public virtual DbSet<VendedorPedidoGrupoProducto> VendedoresPedidosGruposProductos { get; set; }
        public virtual DbSet<Vendedor> Vendedores { get; set; }
        public virtual DbSet<SeguimientoCliente> SeguimientosClientes { get; set; }
        public virtual DbSet<OfertaPermitida> OfertasPermitidas { get; set; }
        public virtual DbSet<OfertaCombinada> OfertasCombinadas { get; set; }
        public virtual DbSet<OfertaCombinadaDetalle> OfertasCombinadasDetalles { get; set; }
        public virtual DbSet<ComisionAnualDetalle> ComisionesAnualesDetalles { get; set; }
        public virtual DbSet<RentingFactura> RentingFacturas { get; set; }
        public virtual DbSet<vstLinPedidoVtaComisionesDetalle> vstLinPedidoVtaComisionesDetalles { get; set; }
        public virtual DbSet<vstLinPedidoVtaComisione> vstLinPedidoVtaComisiones { get; set; }
        public virtual DbSet<LinPedidoCmp> LinPedidoCmps { get; set; }
        public virtual DbSet<PreExtrProducto> PreExtrProductos { get; set; }
        public virtual DbSet<EnvioAgenciaCoordenada> EnviosAgenciasCoordenadas { get; set; }
        public virtual DbSet<CodigoPostal> CodigosPostales { get; set; }
        public virtual DbSet<VendedorCodigoPostalGrupoProducto> VendedoresCodigoPostalGruposProductos { get; set; }
        public virtual DbSet<PersonaContactoCliente> PersonasContactoClientes { get; set; }
        public virtual DbSet<CabFacturaVta> CabsFacturasVtas { get; set; }
        public virtual DbSet<AgenciaTransporte> AgenciasTransportes { get; set; }
        public virtual DbSet<Prepago> Prepagos { get; set; }
        public virtual DbSet<RegaloImportePedido> RegalosImportePedido { get; set; }
        public virtual DbSet<ClasificacionMasVendido> ClasificacionMasVendidos { get; set; }
        public virtual DbSet<CabPedidoCmp> CabPedidosCmp { get; set; }
        public virtual DbSet<ControlStock> ControlesStocks { get; set; }
        public virtual DbSet<OfertaProveedor> OfertasProveedores { get; set; }
        public virtual DbSet<DatoConfirming> DatosConfirmings { get; set; }
        public virtual DbSet<EfectoPedidoVenta> EfectosPedidosVentas { get; set; }
        public virtual DbSet<AgenciaLlamadaWeb> AgenciasLlamadasWeb { get; set; }
        public virtual DbSet<DiarioProducto> DiariosProductos { get; set; }
        public virtual DbSet<EquipoVenta> EquiposVentas { get; set; }
        public virtual DbSet<Kit> Kits { get; set; }
        public virtual DbSet<PreContabilidad> PreContabilidades { get; set; }
        public virtual DbSet<VendedorLinPedidoVta> VendedoresLinPedidoVta { get; set; }
        public virtual DbSet<Contabilidad> Contabilidades { get; set; }
        public virtual DbSet<ApunteBancario> ApuntesBancarios { get; set; }
        public virtual DbSet<FicheroCuaderno43> FicherosCuaderno43 { get; set; }
        public virtual DbSet<RegistroComplementarioConcepto> RegistrosComplementariosConceptos { get; set; }
        public virtual DbSet<RegistroComplementarioEquivalencia> RegistrosComplementariosEquivalencias { get; set; }
        public virtual DbSet<ComisionAnualResumenMes> ComisionesAnualesResumenMes { get; set; }
        public virtual DbSet<MovimientoTPV> MovimientosTPV { get; set; }
        public virtual DbSet<ConciliacionBancariaPunteo> ConciliacionesBancariasPunteos { get; set; }
        public virtual DbSet<CabAlbaranVta> CabsAlbaranesVtas { get; set; }
        public virtual DbSet<Video> Videos { get; set; }
        public virtual DbSet<VideoProducto> VideosProductos { get; set; }
        public virtual DbSet<GruposProducto> GruposProductoes { get; set; }
    
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
