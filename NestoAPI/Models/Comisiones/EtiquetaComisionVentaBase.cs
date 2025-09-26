using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public abstract class EtiquetaComisionVentaBase : EtiquetaComisionBase, IEtiquetaComisionVenta
    {
        public decimal Venta { get; set; }
        public virtual bool SumaEnTotalVenta => false;
        public abstract bool PerteneceALaEtiqueta(vstLinPedidoVtaComisione linea);
        public abstract decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes);
        public abstract decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes, bool incluirPicking);
        public abstract IQueryable<vstLinPedidoVtaComisione> LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking);

        // Para etiquetas de venta, UnidadCifra es "€" (heredado de la base)
    }
}
