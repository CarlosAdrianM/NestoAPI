using NestoAPI.Models.Ganavisiones;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ValidadoresServirJunto
{
    /// <summary>
    /// Interfaz para validadores que determinan si se puede desmarcar ServirJunto.
    /// Issue #141: Patrón extensible para añadir nuevas validaciones.
    /// </summary>
    public interface IValidadorServirJunto
    {
        Task<ValidarServirJuntoResponse> Validar(string almacen, List<ProductoBonificadoConCantidadRequest> productos);
    }
}
