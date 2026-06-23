using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#195 (Fase 3): orquestador de la agrupación por PO.
    /// Pide a la estrategia los grupos de PO listos, los agrupa con el motor y factura cada
    /// pedido destino a través de <c>ServicioFacturas.CrearFactura</c> (único call site del SP
    /// de facturación).
    /// </summary>
    public interface IServicioAgruparPorPO
    {
        /// <summary>
        /// Procesa todos los grupos de PO listos para una empresa: agrupa y factura cada uno.
        /// </summary>
        Task<ResultadoAgrupacionPO> EvaluarYProcesar(string empresa, string usuario);
    }
}
