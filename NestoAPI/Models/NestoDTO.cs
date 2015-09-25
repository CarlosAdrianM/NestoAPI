using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }

    public class LineaPedidoVentaDTO
    {
        public string almacen { get; set; }
        public bool aplicarDescuento { get; set; }
        public short cantidad { get; set; } // era Nullable<short> 
        public string delegacion { get; set; }
        public decimal descuento { get; set; }
        public short estado { get; set; }
        public System.DateTime fechaEntrega { get; set; }
        public string formaVenta { get; set; }
        public string iva { get; set; }
        public decimal precio { get; set; } // era Nullable<decimal> 
        public string producto { get; set; }
        public string texto { get; set; }
        public Nullable<byte> tipoLinea { get; set; }
        public string usuario { get; set; }
        public bool vistoBueno { get; set; }
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
    
}
