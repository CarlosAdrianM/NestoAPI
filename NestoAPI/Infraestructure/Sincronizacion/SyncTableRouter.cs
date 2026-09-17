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

        /// <summary>
        /// NestoAPI#490: los Source con los que publica el PROPIO API (GestorProductos /
        /// GestorClientes: "Nesto" por defecto, "Nesto viejo" desde los jobs de Nesto_sync). El API
        /// está suscrito al mismo topic en el que publica, así que recibe sus propios mensajes; lo
        /// que llevan ya está en la BD y procesarlos solo puede ser un no-op o un daño: el 17/09/26
        /// el nombre en formato oración de #479 (presentación para la tienda) volvió por aquí y
        /// reescribió 676 fichas de Productos. Lista EXACTA, no StartsWith: un módulo externo
        /// podría llamarse "NestoSync" y ese sí hay que procesarlo.
        /// </summary>
        internal static readonly string[] SOURCES_PROPIOS = { "Nesto", "Nesto viejo" };

        /// <summary>¿Es un mensaje que publicó este mismo API (ver <see cref="SOURCES_PROPIOS"/>)?</summary>
        internal static bool EsMensajePropio(string source)
        {
            return !string.IsNullOrWhiteSpace(source)
                && SOURCES_PROPIOS.Contains(source.Trim(), StringComparer.OrdinalIgnoreCase);
        }

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

            // NestoAPI#490: eco de nuestra propia publicación. Se da por procesado (true = ack) sin
            // tocar nada: devolver false lo mandaría a reintentos y a la lista de poison pills.
            if (EsMensajePropio(message.Source))
            {
                Console.WriteLine($"↩️ Mensaje propio (Source={message.Source}) ignorado: ya está en Nesto");
                return true;
            }

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
