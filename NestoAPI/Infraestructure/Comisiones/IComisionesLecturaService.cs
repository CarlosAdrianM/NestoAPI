using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NestoAPI.Models.Comisiones;

namespace NestoAPI.Infraestructure.Comisiones
{
    // Lecturas para el panel de Comisiones del cliente. Se separa del
    // ComisionesController antiguo (cálculo de ResumenComisionesMes) para
    // mantener la superficie testeable acotada.
    public interface IComisionesLecturaService
    {
        Task<ComisionesAntiguasDTO> LeerComisionesAntiguasAsync(string empresa, DateTime fechaDesde, DateTime fechaHasta, string vendedor);
        Task<List<PedidoVendedorComisionDTO>> LeerPedidosVendedorAsync(string vendedor);
        Task<List<VentaVendedorComisionDTO>> LeerVentasVendedorAsync(DateTime fechaDesde, DateTime fechaHasta, string vendedor);
    }
}
