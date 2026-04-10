using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NestoAPI.Models.Informes;

namespace NestoAPI.Infraestructure.Informes
{
    public interface IInformesService
    {
        Task<List<ResumenVentasDTO>> LeerResumenVentasAsync(DateTime fechaDesde, DateTime fechaHasta, bool soloFacturas);
        Task<List<ControlPedidosDTO>> LeerControlPedidosAsync();
    }
}
