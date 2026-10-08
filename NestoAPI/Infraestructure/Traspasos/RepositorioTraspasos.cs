using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models;
using NestoAPI.Models.Traspasos;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Traspasos
{
    /// <summary>
    /// NestoAPI#553: acceso a BD de los traspasos entre almacenes. De momento solo lee la propuesta.
    /// </summary>
    public interface IRepositorioTraspasos
    {
        /// <summary>
        /// La propuesta de prdRellenarReposicionStock2 (NestoAPI#577: sin el freno de «reposición anterior pendiente de
        /// contabilizar» del viejo; lo que ya está en camino se cuenta). Sin hora de corte: es la consulta manual.
        /// </summary>
        Task<List<LineaReposicionStockSP>> LeerPropuesta(string empresa, string origen, string destino);
    }

    public class RepositorioTraspasos : IRepositorioTraspasos
    {
        public async Task<List<LineaReposicionStockSP>> LeerPropuesta(string empresa, string origen, string destino)
        {
            using (var db = new NVEntities())
            {
                // NestoAPI#577: por el único punto de llamada del procedimiento
                return await ProcedimientoPropuestaReposicion
                    .Leer<LineaReposicionStockSP>(db, empresa, origen, destino, corte: null)
                    .ConfigureAwait(false);
            }
        }
    }
}
