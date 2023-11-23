using System.Collections.Generic;

namespace NestoAPI.Models.Comisiones
{
    public interface IServicioComisionesAnuales
    {
        ICollection<ResumenComisionesMes> LeerResumenAnno(IComisionesAnuales comisiones, string vendedor, int anno);
    }
}
