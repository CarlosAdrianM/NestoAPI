using System.Collections.Generic;

namespace NestoAPI.Models.Comisiones
{
    public interface IEtiquetaComision
    {
        string Nombre { get; }
        decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes);
        ICollection<vstLinPedidoVtaComisione> LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta);
    }
}