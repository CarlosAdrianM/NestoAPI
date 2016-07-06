using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

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
    }

    public class FormaPagoDTO
    {
        public string formaPago { get; set; }
        public string descripcion { get; set; }
        public bool bloquearPagos { get; set; }
        public bool cccObligatorio { get; set; }
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
    }

    public class LineaPedidoVentaDTO
    {
        public int id { get; set; }
        public string almacen { get; set; }
        public bool aplicarDescuento { get; set; }
        public short cantidad { get; set; } // era Nullable<short> 
        public string delegacion { get; set; }
        public decimal descuento { get; set; }
        public short estado { get; set; }
        public System.DateTime fechaEntrega { get; set; }
        public string formaVenta { get; set; }
        public string iva { get; set; }
        public Nullable<int> oferta { get; set; }
        public decimal precio { get; set; } // era Nullable<decimal> 
        public string producto { get; set; }
        public string texto { get; set; }
        public Nullable<byte> tipoLinea { get; set; }
        public string usuario { get; set; }
        public bool vistoBueno { get; set; }
        public decimal baseImponible { get; set; }
        public decimal importeIva { get; set; }
        public decimal total { get; set; }
        public decimal descuentoProducto { get; set; }
    }

    public class PedidoVentaDTO
    {
        public PedidoVentaDTO()
        {
            this.LineasPedido = new HashSet<LineaPedidoVentaDTO>();
        }

        public string empresa { get; set; }
        public int numero { get; set; }
        public string cliente { get; set; }
        public string contacto { get; set; }
        public Nullable<System.DateTime> fecha { get; set; }
        public string formaPago { get; set; }
        [Required]
        public string plazosPago { get; set; }
        public Nullable<System.DateTime> primerVencimiento { get; set; }
        public string iva { get; set; }
        public string vendedor { get; set; }
        public string comentarios { get; set; }
        public string comentarioPicking { get; set; }
        public string periodoFacturacion { get; set; }
        public string ruta { get; set; }
        public string serie { get; set; }
        public string ccc { get; set; }
        public string origen { get; set; }
        public string contactoCobro { get; set; }
        public decimal noComisiona { get; set; }
        public bool vistoBuenoPlazosPago { get; set; }
        public bool mantenerJunto { get; set; }
        public bool servirJunto { get; set; }
        public string usuario { get; set; }

        public virtual ICollection<LineaPedidoVentaDTO> LineasPedido { get; set; }
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

    public class PrecioProductoDTO
    {
        public decimal precio { get; set; }
        public decimal descuento { get; set; }
        public bool aplicarDescuento { get; set; }
        public string motivo { get; set; }
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
        public decimal baseImponible { get; set; }
        public decimal total { get; set; }
        public string vendedor { get; set; }
    }

    public class StockProductoDTO
    {
        public int stock { get; set; }
        public int cantidadDisponible { get; set; }
        public string urlImagen { get; set; }
    }

    public class Mod347DTO
    {
        public Mod347DTO()
        {
            this.MovimientosMayor = new HashSet<ExtractoClienteDTO>();
        }

        public decimal[] trimestre { get; set; }
        public decimal total { get { return trimestre[0] + trimestre[1] + trimestre[2] + trimestre[3]; } }
        public string nombre { get; set; }
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
}
