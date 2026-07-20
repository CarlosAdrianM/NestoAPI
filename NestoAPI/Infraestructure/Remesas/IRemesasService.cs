using NestoAPI.Models.Remesas;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Remesas
{
    /// <summary>
    /// Lecturas del módulo de Remesas de cobros (Nesto#340, Fase 1C.14: sustituye los accesos EF
    /// del RemesasViewModel del cliente).
    /// </summary>
    public interface IRemesasService
    {
        /// <param name="top">Número máximo de remesas (null = todas, botón "Ver Todas").</param>
        Task<List<RemesaDTO>> LeerRemesasAsync(string empresa, int? top);
    }
}
