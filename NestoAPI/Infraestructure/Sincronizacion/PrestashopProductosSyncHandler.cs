using NestoAPI.Models.Sincronizacion;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// Handler de sincronización para la tabla PrestashopProductos
    /// Procesa datos personalizados de Prestashop (nombre, descripciones, PVP IVA incluido)
    /// </summary>
    public class PrestashopProductosSyncHandler : ISyncTableHandler<PrestashopProductoSyncMessage>
    {
        public string TableName => "PrestashopProductos";

        // Implementación base polimórfica
        Task<bool> ISyncTableHandlerBase.HandleAsync(SyncMessageBase message)
        {
            return HandleAsync(message as PrestashopProductoSyncMessage);
        }

        string ISyncTableHandlerBase.GetMessageKey(SyncMessageBase message)
        {
            return GetMessageKey(message as PrestashopProductoSyncMessage);
        }

        string ISyncTableHandlerBase.GetLogInfo(SyncMessageBase message)
        {
            return GetLogInfo(message as PrestashopProductoSyncMessage);
        }

        public SyncMessageBase Deserialize(string json, JsonSerializerOptions options)
        {
            return JsonSerializer.Deserialize<PrestashopProductoSyncMessage>(json, options);
        }

        // Implementación tipada
        public string GetMessageKey(PrestashopProductoSyncMessage message)
        {
            var producto = message?.Producto?.Trim() ?? "NULL";
            var source = message?.Source?.Trim() ?? "NULL";
            return $"PRESTASHOPPRODUCTO|{producto}|{source}";
        }

        public string GetLogInfo(PrestashopProductoSyncMessage message)
        {
            var info = $"PrestashopProducto {message?.Producto?.Trim() ?? "NULL"}";

            if (!string.IsNullOrEmpty(message?.NombrePersonalizado))
            {
                info += $" ({message.NombrePersonalizado.Trim()})";
            }

            if (!string.IsNullOrEmpty(message?.Source))
            {
                info += $", Source={message.Source}";
            }

            if (message?.PVP_IVA_Incluido.HasValue == true)
            {
                info += $", PVP_IVA={message.PVP_IVA_Incluido.Value}";
            }

            return info;
        }

        public async Task<bool> HandleAsync(PrestashopProductoSyncMessage message)
        {
            try
            {
                if (message == null)
                {
                    Console.WriteLine("⚠️ Mensaje PrestashopProducto nulo, omitiendo");
                    return false;
                }

                var productoId = message.Producto?.Trim();

                if (string.IsNullOrEmpty(productoId))
                {
                    Console.WriteLine("⚠️ Producto vacío en mensaje PrestashopProducto");
                    return false;
                }

                Console.WriteLine($"📦 PrestashopProducto {productoId} recibido (Source={message.Source}). No hay procesamiento de recepción implementado aún.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error procesando PrestashopProducto: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }
    }
}
