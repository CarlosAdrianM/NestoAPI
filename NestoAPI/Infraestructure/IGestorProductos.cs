using NestoAPI.Models;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Interfaz para el gestor de productos
    /// </summary>
    public interface IGestorProductos
    {
        /// <summary>
        /// Publica un mensaje de sincronización de producto al sistema externo
        /// </summary>
        /// <param name="productoDTO">DTO del producto con toda la información completa</param>
        /// <param name="source">Origen del mensaje (ej: "Nesto viejo")</param>
        Task PublicarProductoSincronizar(ProductoDTO productoDTO, string source = "Nesto", string usuario = null);
    }
}
