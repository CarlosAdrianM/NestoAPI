using NestoAPI.Models.Sincronizacion;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// Interfaz para handlers de sincronización por tabla
    /// Cada tabla (Clientes, Productos, Proveedores, etc.) tiene su propio handler
    /// </summary>
    public interface ISyncTableHandler
    {
        /// <summary>
        /// Nombre de la tabla que este handler procesa
        /// Ejemplo: "Clientes", "Productos", "Proveedores"
        /// </summary>
        string TableName { get; }

        /// <summary>
        /// Procesa un mensaje de sincronización para esta tabla
        /// </summary>
        /// <param name="message">Mensaje deserializado desde sistemas externos</param>
        /// <returns>true si procesó exitosamente, false si hubo error</returns>
        Task<bool> HandleAsync(ExternalSyncMessageDTO message);
    }
}
