using System;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public interface IEtiquetaComision : ICloneable
    {
        string Nombre { get; }
        decimal Venta { 
            get; 
            set; 
        }
        decimal Tipo { get; set; }
        decimal Comision { get; set; }

        decimal SetTipo(TramoComision tramo);
        decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes);
        decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes, bool incluirPicking);
        IQueryable<vstLinPedidoVtaComisione> LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking);
    }    
}