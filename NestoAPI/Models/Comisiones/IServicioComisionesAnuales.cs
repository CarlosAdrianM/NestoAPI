using System.Collections.Generic;

namespace NestoAPI.Models.Comisiones
{
    public interface IServicioComisionesAnuales
    {
        decimal LeerGeneralVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes);
        decimal LeerUnionLaserVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes);
        decimal LeerEvaVisnuVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes);
        decimal LeerOtrosAparatosVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes);
        ICollection<ResumenComisionesMes> LeerResumenAnno(string vendedor, int anno);
        ICollection<TramoComision> LeerTramosComisionMes(string vendedor);
        ICollection<TramoComision> LeerTramosComisionAnno(string vendedor);
    }
}