using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace NestoAPI.Models
{
    public class ClienteDTO
    {
        public string empresa { get; set; }
        public string cliente { get; set; }
        public string contacto { get; set; }
        public bool clientePrincipal { get; set; }
        public string nombre { get; set; }
        public string direccion { get; set; }
        public string poblacion { get; set; }
        public string telefono { get; set; }
        public string vendedor { get; set; }
        public string comentarios { get; set; }
        public string codigoPostal { get; set; }
        public string cifNif { get; set; }
        public string provincia { get; set; }
        public Nullable<short> estado { get; set; }
        public string iva { get; set; }
        public string grupo { get; set; }
        public string periodoFacturacion { get; set; }
        public string ccc { get; set; }
        public string ruta { get; set; }
        public byte copiasAlbaran { get; set; }
        public byte copiasFactura { get; set; }
        public string comentarioRuta { get; set; }
        public string comentarioPicking { get; set; }
        public string web { get; set; }
        public bool albaranValorado { get; set; }
        public string cadena { get; set; }
        public decimal noComisiona { get; set; }
        public bool servirJunto { get; set; }
        public bool mantenerJunto { get; set; }
        public string usuario { get; set; }

        public virtual ICollection<VendedorGrupoProductoDTO> VendedoresGrupoProducto { get; set; }
        public virtual ICollection<PersonaContactoDTO> PersonasContacto { get; set; }
    }
    public class ClienteProductoDTO
    {
        public string Vendedor { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public int Cantidad { get; set; }
        public DateTime UltimaCompra { get; set; }
        public int EstadoMinimo { get; set; }
        public int EstadoMaximo { get; set; }
    }
    public class CCCDTO
    {
        public string empresa { get; set; }
        public string cliente { get; set; }
        public string contacto { get; set; }
        public string numero { get; set; }
        public string pais { get; set; }
        public string entidad { get; set; }
        public string oficina { get; set; }
        public string bic { get; set; }
        public short estado { get; set; }
        public short? tipoMandato { get; set; }
        public DateTime? fechaMandato { get; set; }
        public string ibanFormateado { get; set; }
        public string nombreEntidad { get; set; }
    }
    public class DireccionesEntregaClienteDTO
    {
        public string contacto { get; set; }
        public bool clientePrincipal { get; set; }
        public string nombre { get; set; }
        public string direccion { get; set; }
        public bool esDireccionPorDefecto { get; set; }
        public string poblacion { get; set; }
        public string comentarios { get; set; }
        public string codigoPostal { get; set; }
        public string provincia { get; set; }
        public Nullable<short> estado { get; set; }
        public string iva { get; set; }
        public string comentarioRuta { get; set; }
        public string comentarioPicking { get; set; }
        public decimal noComisiona { get; set; }
        public bool servirJunto { get; set; }
        public bool mantenerJunto { get; set; }
        public string vendedor { get; set; }
        public string periodoFacturacion { get; set; }
        public string ccc { get; set; }
        public string ruta { get; set; }
        public string formaPago { get; set; }
        public string plazosPago { get; set; }
        public bool tieneCorreoElectronico { get; set; }
        public bool tieneFacturacionElectronica { get; set; }
        public string nif { get; set; }
    }
    public class ExtractoClienteDTO
    {
        public int id { get; set; }
        public string empresa { get; set; }
        public int asiento { get; set; }
        public string cliente { get; set; }
        public string contacto { get; set; }
        public System.DateTime fecha { get; set; }
        public string tipo { get; set; }
        public string documento { get; set; }
        public string efecto { get; set; }
        public string concepto { get; set; }
        public decimal importe { get; set; }
        public decimal importePendiente { get; set; }
        public string vendedor { get; set; }
        public Nullable<System.DateTime> vencimiento { get; set; }
        public string ccc { get; set; }
        public string ruta { get; set; }
        public string estado { get; set; }
        public string formaPago { get; set; }
        public string formaVenta { get; set; }
        public string delegacion { get; set; }
        public string usuario { get; set; }
    }
    public class FormaPagoDTO
    {
        public string formaPago { get; set; }
        public string descripcion { get; set; }
        public bool bloquearPagos { get; set; }
        public bool cccObligatorio { get; set; }
    }
    public class StockAlmacenDTO
    {
        public string almacen { get; set; }
        public int stock { get; set; }
        public int cantidadDisponible { get; set; }
    }
    public class LineaPlantillaVenta
    {
        public string producto { get; set; }
        public string texto { get; set; }
        public short cantidad { get; set; }
        public short cantidadOferta { get; set; }
        public Nullable<short> tamanno { get; set; }
        public string unidadMedida { get; set; }
        public string familia { get; set; }
        public string subGrupo { get; set; }
        /// <summary>
        /// Grupo del producto (ej: COS, ACC, PEL, APA).
        /// Issue #94: Sistema Ganavisiones - necesario para calcular base imponible bonificable.
        /// </summary>
        public string grupo { get; set; }
        public string codigoBarras { get; set; }
        public Nullable<short> estado { get; set; }
        public bool yaFacturado { get; set; }
        public int cantidadVendida { get; set; }
        public int cantidadAbonada { get; set; }
        public Nullable<System.DateTime> fechaUltimaVenta { get; set; }
        public string iva { get; set; }
        public decimal precio { get; set; }
        public decimal descuento { get; set; }
        public bool aplicarDescuento { get; set; }
        public short stock { get; set; }
        public short cantidadDisponible { get; set; }
        public int clasificacionMasVendidos { get; set; }
        public List<StockAlmacenDTO> stocks { get; set; }
    }
    public class PersonaContactoDTO
    {
        public int Numero { get; set; }
        public string Nombre { get; set; }
        [EmailAddress]
        public string CorreoElectronico { get; set; }
        public bool FacturacionElectronica { get; set; }
        public string Telefono { get; set; }
    }
    public class PlazoPagoDTO
    {
        public string plazoPago { get; set; }
        public string descripcion { get; set; }
        public short numeroPlazos { get; set; }
        public short diasPrimerPlazo { get; set; }
        public short diasEntrePlazos { get; set; }
        public short mesesPrimerPlazo { get; set; }
        public short mesesEntrePlazos { get; set; }
        public decimal descuentoPP { get; set; }
        public decimal? financiacion { get; set; }
    }

    public class PlazosPagoResponse
    {
        public List<PlazoPagoDTO> PlazosPago { get; set; }
        public InfoDeudaClienteDTO InfoDeuda { get; set; }
        public string PlazoPagoRecomendado { get; set; }
    }

    public class CondicionesPagoResponse
    {
        public List<PlazoPagoDTO> PlazosPago { get; set; }
        public List<FormaPagoDTO> FormasPago { get; set; }
        public InfoDeudaClienteDTO InfoDeuda { get; set; }
        public string PlazoPagoRecomendado { get; set; }
        public string FormaPagoRecomendada { get; set; }
    }

    public class InfoDeudaClienteDTO
    {
        public bool TieneDeudaVencida { get; set; }
        public decimal? ImporteDeudaVencida { get; set; }
        public int? DiasVencimiento { get; set; }
        public bool TieneImpagados { get; set; }
        public decimal? ImporteImpagados { get; set; }
        public string MotivoRestriccion { get; set; }  // "Impagados", "Deuda vencida", null
    }

    public class PrecioProductoDTO
    {
        public decimal precio { get; set; }
        public decimal descuento { get; set; }
        public bool aplicarDescuento { get; set; }
        public string motivo { get; set; }
    }
    public class ProductoPlantillaDTO
    {
        private readonly NVEntities db;
        public ProductoPlantillaDTO() { }

        // TO DO: cambiar db por gestorStocks

        public ProductoPlantillaDTO(string producto, NVEntities db)
        {
            this.producto = producto;
            this.db = db;
        }
        public string producto { get; set; }
        public string nombre { get; set; }
        public decimal precio { get; set; }
        public bool aplicarDescuento { get; set; }
        public decimal descuento { get; set; }
        public string iva { get; set; }

        public int Stock()
        {
            return db == null
                ? 0
                : db.ExtractosProducto.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == Constantes.Productos.ALMACEN_POR_DEFECTO && e.Número == producto).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).Sum();
        }
        public int Stock(string almacen)
        {
            return db == null
                ? 0
                : db.ExtractosProducto.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Número == producto).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).Sum();
        }
        public int CantidadReservada()
        {
            return db == null
                ? 0
                : db.LinPedidoVtas.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == Constantes.Productos.ALMACEN_POR_DEFECTO && e.Producto == producto && (e.Estado == Constantes.EstadosLineaVenta.EN_CURSO || e.Estado == Constantes.EstadosLineaVenta.PENDIENTE)).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).Sum();
        }
        public int CantidadReservada(string almacen)
        {
            return db == null
                ? 0
                : db.LinPedidoVtas.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Producto == producto && (e.Estado == Constantes.EstadosLineaVenta.EN_CURSO || e.Estado == Constantes.EstadosLineaVenta.PENDIENTE)).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).Sum();
        }
        public int CantidadDisponible()
        {
            return db == null ? 0 : Stock() - CantidadReservada();
        }
        public int CantidadDisponible(string almacen)
        {
            return db == null ? 0 : Stock(almacen) - CantidadReservada(almacen);
        }
        public int CantidadPendienteRecibir()
        {
            return db == null
                ? 0
                : db.LinPedidoCmps.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Producto == producto && (e.Estado == Constantes.EstadosLineaVenta.EN_CURSO || e.Estado == Constantes.EstadosLineaVenta.PENDIENTE)).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).Sum();
        }
    }
    public class SeguimientoClienteDTO
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public DateTime Fecha { get; set; }
        public string Tipo { get; set; }
        public string Vendedor { get; set; }
        public bool Pedido { get; set; }
        public bool ClienteNuevo { get; set; }
        public bool Aviso { get; set; }
        public bool Aparatos { get; set; }
        public bool GestionAparatos { get; set; }
        public bool PrimeraVisita { get; set; }
        public string Comentarios { get; set; }
        public EstadoSeguimientoDTO Estado { get; set; }
        public string Usuario { get; set; }
        public TiposCentro TipoCentro { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public int? NumOrdenExtracto { get; set; }

        public enum TiposCentro
        {
            NoSeSabe,
            SoloEstetica,
            SoloPeluqueria,
            EsteticaYPeluqueria
        }

        public enum EstadoSeguimientoDTO
        {
            Nulo = -1,
            Vigente,
            No_Contactado,
            Gestion_Administrativa
        }

        public class TipoSeguimientoDTO
        {
            public const string TELEFONO = "T";
            public const string VISITA = "V";
        }
    }
    public class ResumenPedidoVentaDTO
    {
        public string empresa { get; set; }
        public int numero { get; set; }
        public string cliente { get; set; }
        public string contacto { get; set; }
        public string nombre { get; set; }
        public string direccion { get; set; }
        public string codPostal { get; set; }
        public string poblacion { get; set; }
        public string provincia { get; set; }
        public Nullable<System.DateTime> fecha { get; set; }
        public bool tieneProductos { get; set; }
        public bool tienePendientes { get; set; }
        public bool tienePicking { get; set; }
        public bool tieneFechasFuturas { get; set; }
        public bool tienePresupuesto { get; set; }
        public decimal baseImponible { get; set; }
        public decimal total { get; set; }
        public string vendedor { get; set; }
        public string ruta { get; set; }
        public string ultimoSeguimiento { get; set; }
    }
    public class StockProductoPlantillaDTO
    {
        public int stock { get; set; }
        public int cantidadDisponible { get; set; }
        public int cantidadPendienteRecibir { get; set; }
        public string urlImagen { get; set; }
        public int StockDisponibleTodosLosAlmacenes { get; set; }
    }
    public class Mod347DTO
    {
        public Mod347DTO()
        {
            MovimientosMayor = new HashSet<ExtractoClienteDTO>();
        }

        public decimal[] trimestre { get; set; }
        public decimal total => trimestre[0] + trimestre[1] + trimestre[2] + trimestre[3];
        public string nombre { get; set; }
        public string cifNif { get; set; }
        public string direccion { get; set; }
        public string codigoPostal { get; set; }

        public virtual ICollection<ExtractoClienteDTO> MovimientosMayor { get; set; }
    }
    public class UltimasVentasProductoClienteDTO
    {
        public DateTime fecha { get; set; }
        public short cantidad { get; set; }
        public decimal precioBruto { get; set; }
        public decimal descuentos { get; set; }
        public decimal precioNeto { get; set; }
    }
    public class VendedorDTO
    {
        public string vendedor { get; set; }
        public string nombre { get; set; }
        public int estado { get; set; }
    }
    public class VendedorGrupoProductoDTO
    {
        public string vendedor { get; set; }
        public short estado { get; set; }
        public string grupoProducto { get; set; }
        public string usuario { get; set; }
    }
}
