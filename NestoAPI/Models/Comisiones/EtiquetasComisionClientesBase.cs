using System.Collections.Generic;

namespace NestoAPI.Models.Comisiones
{
    public abstract class EtiquetaComisionClientesBase : EtiquetaComisionBase, IEtiquetaComisionClientes
    {
        public int Recuento { get; set; }
        public abstract int LeerClientesMes(string vendedor, int anno, int mes);
        public abstract List<ClienteVenta> LeerClientesDetalle(string vendedor, int anno, int mes);

        // Sobrescribir para etiquetas de clientes
        public override string UnidadCifra => "clientes";
    }
}
