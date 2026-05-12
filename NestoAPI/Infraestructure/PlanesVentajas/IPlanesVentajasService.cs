using System.Collections.Generic;
using System.Threading.Tasks;
using NestoAPI.Models.PlanesVentajas;

namespace NestoAPI.Infraestructure.PlanesVentajas
{
    public interface IPlanesVentajasService
    {
        Task<List<EstadoPlanVentajasDTO>> ListarEstadosAsync();
        Task<List<EmpresaResumenDTO>> ListarEmpresasAsync();
        Task<List<PlanVentajasDTO>> ListarPlanesAsync(string vendedor, string filtroCliente, bool incluirCancelados);
        Task<PlanVentajasDTO> ObtenerPlanAsync(int numero);
        Task<List<ClientePlanVentajasDTO>> ObtenerClientesAsync(int numero, string empresa);
        Task<List<LineaVentaPlanDTO>> ObtenerLineasVentaAsync(int numero, string empresa);
        Task<PlanVentajasDTO> CrearPlanAsync(PlanVentajasDTO plan, string usuario);
        Task<PlanVentajasDTO> ActualizarPlanAsync(int numero, PlanVentajasDTO plan, string usuario);
    }
}
