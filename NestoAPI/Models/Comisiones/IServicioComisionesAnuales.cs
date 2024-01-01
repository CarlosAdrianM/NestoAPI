using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public interface IServicioComisionesAnuales
    {
        NVEntities Db { get; }
        //ICollection<ResumenComisionesMes> LeerResumenAnno(ICollection<IEtiquetaComision> etiquetas, string vendedor, int anno);
        List<string> ListaVendedores(string vendedor);
        decimal CalcularVentaFiltrada(bool incluirAlbaranes, DateTime fechaDesde, DateTime fechaHasta, ref IQueryable<vstLinPedidoVtaComisione> consulta, bool incluirPicking);
        IQueryable<vstLinPedidoVtaComisione> ConsultaVentaFiltrada(bool incluirAlbaranes, DateTime fechaDesde, DateTime fechaHasta, ref IQueryable<vstLinPedidoVtaComisione> consulta, bool incluirPicking);
        List<ClienteVenta> LeerClientesConVenta(string vendedor, int anno, int mes);
        List<ClienteVenta> LeerClientesNuevosConVenta(string vendedor, int anno, int mes);
        List<ComisionAnualResumenMes> LeerComisionesAnualesResumenMes(List<string> listaVendedores, int anno);
    }
}
