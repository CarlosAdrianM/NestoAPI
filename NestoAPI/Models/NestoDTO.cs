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
}
