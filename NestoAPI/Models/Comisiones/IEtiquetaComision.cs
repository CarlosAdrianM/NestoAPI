using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public interface IEtiquetaComision : ICloneable
    {
        string Nombre { get; }        
        decimal Tipo { get; set; }
        decimal Comision { get; set; }
        // Se utiliza para especificar si es el tipo comisión, el tipo especial, un fijo más tipo especial, una porción del tipo especial, etc.
        decimal SetTipo(TramoComision tramo); 
        // Cuando no es acumulada la comisión se calcula venta * tipo (el set de Comision da error)
        // C.C.: se calcula la que debería llevar y se resta la que ya se ha pagado (el set de Comision no da error)
        bool EsComisionAcumulada { get; }
    }

    public interface IEtiquetaComisionVenta : IEtiquetaComision
    {
        bool PerteneceALaEtiqueta(vstLinPedidoVtaComisione linea);
        decimal Venta { get; set; }
        decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes);
        decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes, bool incluirPicking);
        IQueryable<vstLinPedidoVtaComisione> LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking);
    }

    public interface IEtiquetaComisionClientes : IEtiquetaComision
    {
        int Recuento { get; set; }
        int LeerClientesMes(string vendedor, int anno, int mes); 
        List<ClienteVenta> LeerClientesDetalle(string vendedor, int anno, int mes);
    } 
}