using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public interface IComisionesAnuales
    {
        ICollection<IEtiquetaComision> Etiquetas { get; }
        ICollection<ResumenComisionesMes> LeerResumenAnno(string vendedor, int anno, bool todoElEquipo = false);
        ICollection<TramoComision> LeerTramosComisionMes(string vendedor);
        ICollection<TramoComision> LeerTramosComisionAnno(string vendedor);
        ICollection<IEtiquetaComision> NuevasEtiquetas { get; }
        ICalculadorProyecciones CalculadorProyecciones { get; }
        string EtiquetaLinea(vstLinPedidoVtaComisione linea);
    }

    //public static class ComisionesAnualesExtensions
    //{
    //    public static ICollection<IEtiquetaComision> NuevasEtiquetas(this IComisionesAnuales comisionesAnuales)
    //    {
    //        return comisionesAnuales.Etiquetas.Select(etiqueta => (IEtiquetaComision)etiqueta.Clone()).ToList();
    //    }
    //}
}