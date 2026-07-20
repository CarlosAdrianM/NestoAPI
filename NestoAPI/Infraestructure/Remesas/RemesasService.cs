using NestoAPI.Models;
using NestoAPI.Models.Remesas;
using System.Collections.Generic;
using System.Data.Entity;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Remesas
{
    public class RemesasService : IRemesasService
    {
        // Nesto#340 Fase 1C.14 slice 2: la tabla Remesas NO está mapeada en el EDMX de NestoAPI,
        // pero no hace falta mapearla (mismo patrón que Cargos/EstadosCCC/ExtractoInmovilizado):
        // SQL crudo aliasando la columna con acento a ASCII para materializar el DTO directamente.
        public async Task<List<RemesaDTO>> LeerRemesasAsync(string empresa, int? top)
        {
            using (NVEntities db = new NVEntities())
            {
                List<RemesaDTO> remesas = top.HasValue
                    ? await db.Database.SqlQuery<RemesaDTO>(
                        "SELECT TOP (@p1) [Número] AS Numero, Fecha, Importe, Banco FROM Remesas WHERE Empresa = @p0 ORDER BY [Número] DESC",
                        empresa, top.Value).ToListAsync().ConfigureAwait(false)
                    : await db.Database.SqlQuery<RemesaDTO>(
                        "SELECT [Número] AS Numero, Fecha, Importe, Banco FROM Remesas WHERE Empresa = @p0 ORDER BY [Número] DESC",
                        empresa).ToListAsync().ConfigureAwait(false);

                foreach (RemesaDTO remesa in remesas)
                {
                    remesa.Banco = remesa.Banco?.Trim();
                }
                return remesas;
            }
        }
    }
}
