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
    
    public partial class Empresa
    {
        public Empresa()
        {
            this.Empresas1 = new HashSet<Empresa>();
            this.EnviosAgencias = new HashSet<EnviosAgencia>();
            this.LinPedidoVtas = new HashSet<LinPedidoVta>();
            this.Clientes = new HashSet<Cliente>();
            this.Productos = new HashSet<Producto>();
            this.Familias = new HashSet<Familia>();
            this.CondPagoClientes = new HashSet<CondPagoCliente>();
            this.DescuentosClientes = new HashSet<DescuentosCliente>();
            this.DescuentosProductoes = new HashSet<DescuentosProducto>();
            this.DíasPagoClientes = new HashSet<DíasPagoClientes>();
            this.FormasPagoes = new HashSet<FormaPago>();
            this.PlazosPagoes = new HashSet<PlazoPago>();
            this.ParámetrosIVA = new HashSet<ParametroIVA>();
            this.ParámetrosUsuario = new HashSet<ParametroUsuario>();
            this.ExtractoProductoes = new HashSet<ExtractoProducto>();
            this.ExtractoProveedors = new HashSet<ExtractoProveedor>();
            this.ExtractoProveedors1 = new HashSet<ExtractoProveedor>();
            this.PersonasContactoProveedors = new HashSet<PersonaContactoProveedor>();
            this.Proveedores = new HashSet<Proveedor>();
            this.CabFacturaCmps = new HashSet<CabFacturaCmp>();
            this.CabFacturaCmps1 = new HashSet<CabFacturaCmp>();
            this.FormasVentas = new HashSet<FormaVenta>();
            this.InventariosCuadres = new HashSet<InventarioCuadre>();
            this.Inventarios = new HashSet<Inventario>();
        }
    
        public string Número { get; set; }
        public string Nombre { get; set; }
        public string NIF { get; set; }
        public string Sufijo { get; set; }
        public bool MostrarMenú { get; set; }
        public string IVA_por_defecto { get; set; }
        public string CtaDtoCliente { get; set; }
        public string CtaDescuentoVta { get; set; }
        public string CtaDtoPPVta { get; set; }
        public string CtaDtoProveedor { get; set; }
        public string CtaDescuentoCmp { get; set; }
        public string CtaDtoPPCmp { get; set; }
        public string DelegaciónVarios { get; set; }
        public string FormaVentaVarios { get; set; }
        public string CtaCaja { get; set; }
        public string CtaDescuadre { get; set; }
        public Nullable<byte> LongitudCuenta { get; set; }
        public string FormaPagoTalón { get; set; }
        public string FormaPagoEfectivo { get; set; }
        public string FormaPagoTarjeta { get; set; }
        public string FormaPagoPagaré { get; set; }
        public string PlazosPagoDefecto { get; set; }
        public string VendedorVarios { get; set; }
        public string Dirección { get; set; }
        public string Dirección2 { get; set; }
        public string Teléfono { get; set; }
        public string Fax { get; set; }
        public string CodPostal { get; set; }
        public string Población { get; set; }
        public string Provincia { get; set; }
        public string Texto { get; set; }
        public string Logotipo { get; set; }
        public string TipoIvaDefecto { get; set; }
        public bool GeneraBonificaciónDefecto { get; set; }
        public byte VigenciaBonificación { get; set; }
        public string CtaRegularización { get; set; }
        public string CtaRemanente { get; set; }
        public string CtaDiferenciasNegativasEjerciciosAnteriores { get; set; }
        public string CtaAPagarIVA { get; set; }
        public string CtaACompensarIVA { get; set; }
        public string TipoIvaDefectoSoportado { get; set; }
        public string ProductoRegularizaciones { get; set; }
        public string ProductoReparacion { get; set; }
        public string Email { get; set; }
        public string Web { get; set; }
        public string EmpresaDatafono { get; set; }
        public bool EmpresaCurso { get; set; }
        public string EstadoReembolsado { get; set; }
        public string ProveedorCostes { get; set; }
        public string ctaDiferenciasEnCompras { get; set; }
        public int UltAsientoContable { get; set; }
        public string ProveedorIRPF { get; set; }
        public string ContactoProveedorIRPF { get; set; }
        public decimal PorcentajeFacturadoNoEntregadoParaPedidoCmpAuto { get; set; }
        public string TextoFactura { get; set; }
        public int StockAntiguo { get; set; }
        public Nullable<int> EstadoProductoRegalo { get; set; }
        public string InformeAlbaran { get; set; }
        public string InformeFactura { get; set; }
        public string InformePedidoCmp { get; set; }
        public string InformeCartaPagare { get; set; }
        public string InformeCartaPagareManual { get; set; }
        public string ImpresoraPagare { get; set; }
        public bool OcultarB { get; set; }
        public bool MostrarOperador { get; set; }
        public Nullable<int> MaxPickingListado { get; set; }
        public Nullable<int> MaxPickingPorListar { get; set; }
        public string InformeFacturaConMembrete { get; set; }
        public bool VerFormaVentaCng { get; set; }
        public Nullable<decimal> PreciosRenting36 { get; set; }
        public Nullable<decimal> PreciosRenting48 { get; set; }
        public Nullable<decimal> PreciosRentingImporte { get; set; }
        public Nullable<decimal> PorcentajeVR36 { get; set; }
        public Nullable<decimal> PorcentajeVR48 { get; set; }
        public Nullable<decimal> PorcentajeRentingProductos { get; set; }
        public Nullable<decimal> PorcentajeRentingServicios { get; set; }
        public bool EnviarCorreoProductoNuevo { get; set; }
        public string ctaTrabajosRealizados { get; set; }
        public Nullable<System.DateTime> FechaPicking { get; set; }
        public bool BloquearTraspasoRuta { get; set; }
        public string RutaImpagado { get; set; }
        public string Usuario { get; set; }
        public System.DateTime Fecha_Modificación { get; set; }
    
        public virtual ICollection<Empresa> Empresas1 { get; set; }
        public virtual Empresa Empresa1 { get; set; }
        public virtual ICollection<EnviosAgencia> EnviosAgencias { get; set; }
        public virtual ICollection<LinPedidoVta> LinPedidoVtas { get; set; }
        public virtual ICollection<Cliente> Clientes { get; set; }
        public virtual ICollection<Producto> Productos { get; set; }
        public virtual ICollection<Familia> Familias { get; set; }
        public virtual ICollection<CondPagoCliente> CondPagoClientes { get; set; }
        public virtual ICollection<DescuentosCliente> DescuentosClientes { get; set; }
        public virtual ICollection<DescuentosProducto> DescuentosProductoes { get; set; }
        public virtual ICollection<DíasPagoClientes> DíasPagoClientes { get; set; }
        public virtual FormaPago FormasPago { get; set; }
        public virtual FormaPago FormasPago1 { get; set; }
        public virtual FormaPago FormasPago2 { get; set; }
        public virtual PlazoPago PlazosPago { get; set; }
        public virtual ICollection<FormaPago> FormasPagoes { get; set; }
        public virtual ICollection<PlazoPago> PlazosPagoes { get; set; }
        public virtual ICollection<ParametroIVA> ParámetrosIVA { get; set; }
        public virtual ICollection<ParametroUsuario> ParámetrosUsuario { get; set; }
        public virtual ICollection<ExtractoProducto> ExtractoProductoes { get; set; }
        public virtual ICollection<ExtractoProveedor> ExtractoProveedors { get; set; }
        public virtual ICollection<ExtractoProveedor> ExtractoProveedors1 { get; set; }
        public virtual Proveedor Proveedore { get; set; }
        public virtual ICollection<PersonaContactoProveedor> PersonasContactoProveedors { get; set; }
        public virtual ICollection<Proveedor> Proveedores { get; set; }
        public virtual ICollection<CabFacturaCmp> CabFacturaCmps { get; set; }
        public virtual ICollection<CabFacturaCmp> CabFacturaCmps1 { get; set; }
        public virtual FormaVenta FormasVenta { get; set; }
        public virtual ICollection<FormaVenta> FormasVentas { get; set; }
        public virtual ICollection<InventarioCuadre> InventariosCuadres { get; set; }
        public virtual ICollection<Inventario> Inventarios { get; set; }
    }
}
