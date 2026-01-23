using NestoAPI.Models.Mayor;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Contabilidad
{
    /// <summary>
    /// Interface para adaptar extractos de cliente o proveedor a MovimientoMayorDTO.
    /// Implementa el patr patron Adapter para unificar ambas estructuras.
    /// </summary>
    public interface IExtractoAdapter<T>
    {
        /// <summary>
        /// Adapta una lista de extractos a movimientos del Mayor.
        /// </summary>
        /// <param name="extractos">Lista de extractos (ExtractoCliente o ExtractoProveedor)</param>
        /// <returns>Lista de movimientos del Mayor</returns>
        IEnumerable<MovimientoMayorDTO> Adaptar(IEnumerable<T> extractos);
    }
}
