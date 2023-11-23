using System.Collections.Generic;

namespace NestoAPI.Models.Comisiones
{
    public interface IComisionesAnuales
    {
        ICollection<IEtiquetaComision> Etiquetas { get; }
        ICollection<ResumenComisionesMes> LeerResumenAnno(string vendedor, int anno);
        ICollection<TramoComision> LeerTramosComisionMes(string vendedor);
        ICollection<TramoComision> LeerTramosComisionAnno(string vendedor);
        ICollection<IEtiquetaComision> NuevasEtiquetas { get; }
        ICalculadorProyecciones CalculadorProyecciones { get; }
        string EtiquetaLinea(vstLinPedidoVtaComisione linea);
    }
}