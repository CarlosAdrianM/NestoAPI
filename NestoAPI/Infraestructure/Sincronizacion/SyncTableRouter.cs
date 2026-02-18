using NestoAPI.Models.Sincronizacion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// Router que dirige mensajes de sincronización al handler correcto según la tabla
    /// Patrón: Strategy + Factory
    /// </summary>
    public class SyncTableRouter
    {
        private readonly Dictionary<string, ISyncTableHandlerBase> _handlers;

        public SyncTableRouter(IEnumerable<ISyncTableHandlerBase> handlers)
        {
            _handlers = handlers.ToDictionary(h => h.TableName, h => h, StringComparer.OrdinalIgnoreCase);

            Console.WriteLine($"📋 SyncTableRouter inicializado con {_handlers.Count} handlers:");
            foreach (var tableName in _handlers.Keys)
            {
                Console.WriteLine($"   - {tableName}");
            }
        }

        /// <summary>
        /// Registra un handler manualmente (útil para testing o carga dinámica)
        /// </summary>
        public void RegisterHandler(ISyncTableHandlerBase handler)
        {
            _handlers[handler.TableName] = handler;
            Console.WriteLine($"✅ Handler registrado: {handler.TableName}");
        }

        /// <summary>
        /// Procesa un mensaje rutándolo al handler correcto según la tabla
        /// </summary>
        public async Task<bool> RouteAsync(SyncMessageBase message)
        {
            if (message == null)
            {
                Console.WriteLine("⚠️ Mensaje nulo recibido");
                return false;
            }

            if (string.IsNullOrWhiteSpace(message.Tabla))
            {
                Console.WriteLine("⚠️ Mensaje sin tabla especificada");
                return false;
            }

            Console.WriteLine($"📥 Mensaje recibido: Tabla={message.Tabla}, Source={message.Source}");

            if (!_handlers.ContainsKey(message.Tabla))
            {
                Console.WriteLine($"⚠️ No hay handler registrado para tabla '{message.Tabla}'");
                Console.WriteLine($"   Handlers disponibles: {string.Join(", ", _handlers.Keys)}");
                return false;
            }

            var handler = _handlers[message.Tabla];

            try
            {
                return await handler.HandleAsync(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error en handler de tabla '{message.Tabla}': {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Retorna la lista de tablas soportadas
        /// </summary>
        public IEnumerable<string> GetSupportedTables()
        {
            return _handlers.Keys;
        }

        /// <summary>
        /// Obtiene el handler apropiado para un mensaje (basado en la tabla)
        /// </summary>
        public ISyncTableHandlerBase GetHandler(SyncMessageBase message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.Tabla))
            {
                return null;
            }

            return _handlers.ContainsKey(message.Tabla) ? _handlers[message.Tabla] : null;
        }

        /// <summary>
        /// Obtiene el handler por nombre de tabla
        /// </summary>
        public ISyncTableHandlerBase GetHandler(string tableName)
        {
            if (string.IsNullOrEmpty(tableName))
            {
                return null;
            }

            return _handlers.ContainsKey(tableName) ? _handlers[tableName] : null;
        }
    }
}
