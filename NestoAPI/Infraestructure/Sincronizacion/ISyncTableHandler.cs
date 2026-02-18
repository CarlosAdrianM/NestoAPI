using NestoAPI.Models.Sincronizacion;
using System.Text.Json;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// Interfaz base no genérica para handlers de sincronización
    /// Permite almacenar handlers de diferentes tipos en colecciones
    /// </summary>
    public interface ISyncTableHandlerBase
    {
        /// <summary>
        /// Nombre de la tabla que este handler procesa
        /// </summary>
        string TableName { get; }

        /// <summary>
        /// Procesa un mensaje de sincronización (versión polimórfica)
        /// </summary>
        Task<bool> HandleAsync(SyncMessageBase message);

        /// <summary>
        /// Genera una clave única para detección de duplicados (versión polimórfica)
        /// </summary>
        string GetMessageKey(SyncMessageBase message);

        /// <summary>
        /// Genera información descriptiva para logs (versión polimórfica)
        /// </summary>
        string GetLogInfo(SyncMessageBase message);

        /// <summary>
        /// Deserializa un JSON al tipo de mensaje específico de este handler
        /// </summary>
        SyncMessageBase Deserialize(string json, JsonSerializerOptions options);
    }

    /// <summary>
    /// Interfaz genérica para handlers de sincronización por tabla
    /// Cada tabla (Clientes, Productos, Proveedores, etc.) tiene su propio handler
    /// </summary>
    /// <typeparam name="TMessage">Tipo específico de mensaje que hereda de SyncMessageBase</typeparam>
    public interface ISyncTableHandler<TMessage> : ISyncTableHandlerBase where TMessage : SyncMessageBase
    {
        /// <summary>
        /// Procesa un mensaje de sincronización para esta tabla (versión tipada)
        /// </summary>
        /// <param name="message">Mensaje deserializado desde sistemas externos</param>
        /// <returns>true si procesó exitosamente, false si hubo error</returns>
        Task<bool> HandleAsync(TMessage message);

        /// <summary>
        /// Genera una clave única para detección de duplicados (versión tipada)
        /// Ejemplo: "CLIENTE|12345|0|Odoo" o "PRODUCTO|17404|Odoo"
        /// </summary>
        /// <param name="message">Mensaje recibido</param>
        /// <returns>Clave única que identifica el mensaje</returns>
        string GetMessageKey(TMessage message);

        /// <summary>
        /// Genera información descriptiva para logs (versión tipada)
        /// Ejemplo: "Cliente 12345, Contacto 0, Source=Odoo" o "Producto 17404 (Nombre), PVP=12.50"
        /// </summary>
        /// <param name="message">Mensaje recibido</param>
        /// <returns>String descriptivo para logging</returns>
        string GetLogInfo(TMessage message);
    }
}
