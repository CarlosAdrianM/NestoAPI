using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Data.Entity;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// Gestor centralizado de reintentos de mensajes de sincronización
    /// Previene bucles infinitos en mensajes fallidos de Pub/Sub
    /// </summary>
    public class MessageRetryManager
    {
        private readonly NVEntities _db;
        private const int MaxAttempts = 5;

        public MessageRetryManager(NVEntities db)
        {
            _db = db;
        }

        /// <summary>
        /// Verifica si un mensaje debe procesarse o debe ser descartado (poison pill)
        /// </summary>
        /// <param name="messageId">ID único del mensaje de Pub/Sub</param>
        /// <returns>
        /// true: mensaje puede procesarse
        /// false: mensaje es poison pill y debe retornar 200 sin procesar
        /// </returns>
        public async Task<bool> ShouldProcessMessage(string messageId)
        {
            var retryRecord = await _db.SyncMessageRetries
                .FirstOrDefaultAsync(r => r.MessageId == messageId);

            if (retryRecord == null)
            {
                // Primera vez que vemos este mensaje, debe procesarse
                return true;
            }

            var status = retryRecord.StatusEnum;

            switch (status)
            {
                case RetryStatus.Retrying:
                    // Aún dentro del límite de reintentos
                    return retryRecord.AttemptCount < MaxAttempts;

                case RetryStatus.PoisonPill:
                    // Ya llegó al límite y está pendiente de revisión
                    Console.WriteLine($"🚫 Poison pill detectado: MessageId={messageId}, Attempts={retryRecord.AttemptCount}");
                    return false;

                case RetryStatus.Reprocess:
                    // Marcado para reprocesar (se reseteará el contador)
                    Console.WriteLine($"🔄 Mensaje marcado para reprocesar: MessageId={messageId}");
                    return true;

                case RetryStatus.Resolved:
                    // Ya fue marcado como resuelto, no procesar
                    Console.WriteLine($"✅ Mensaje ya resuelto: MessageId={messageId}");
                    return false;

                case RetryStatus.PermanentFailure:
                    // Marcado como fallo permanente, no procesar
                    Console.WriteLine($"❌ Fallo permanente: MessageId={messageId}");
                    return false;

                default:
                    // Estado desconocido, permitir procesamiento pero loggear
                    Console.WriteLine($"⚠️ Estado desconocido: MessageId={messageId}, Status={status}");
                    return true;
            }
        }

        /// <summary>
        /// Registra un intento de procesamiento de mensaje
        /// </summary>
        /// <param name="messageId">ID único del mensaje</param>
        /// <param name="message">Mensaje completo (para almacenar en JSON)</param>
        public async Task RecordAttempt(string messageId, ExternalSyncMessageDTO message)
        {
            var retryRecord = await _db.SyncMessageRetries
                .FirstOrDefaultAsync(r => r.MessageId == messageId);

            var now = DateTime.UtcNow;

            if (retryRecord == null)
            {
                // Primer intento - crear registro
                retryRecord = new SyncMessageRetry
                {
                    MessageId = messageId,
                    Tabla = message?.Tabla ?? "Unknown",
                    EntityId = GetEntityId(message),
                    Source = message?.Source ?? "Unknown",
                    AttemptCount = 1,
                    FirstAttemptDate = now,
                    LastAttemptDate = now,
                    Status = RetryStatus.Retrying.ToString(),
                    MessageData = SerializeMessage(message)
                };

                _db.SyncMessageRetries.Add(retryRecord);
            }
            else if (retryRecord.StatusEnum == RetryStatus.Reprocess)
            {
                // Marcado para reprocesar: resetear contador y cambiar a Retrying
                retryRecord.AttemptCount = 1;
                retryRecord.LastAttemptDate = now;
                retryRecord.Status = RetryStatus.Retrying.ToString();
                retryRecord.LastError = null;

                Console.WriteLine($"🔄 Reprocesando mensaje: MessageId={messageId}, contador reseteado");
            }
            else
            {
                // Incrementar contador de intentos
                retryRecord.AttemptCount++;
                retryRecord.LastAttemptDate = now;

                // Si alcanzó el límite, marcar como PoisonPill
                if (retryRecord.AttemptCount >= MaxAttempts && retryRecord.StatusEnum == RetryStatus.Retrying)
                {
                    retryRecord.Status = RetryStatus.PoisonPill.ToString();
                    Console.WriteLine($"☠️ Mensaje convertido en poison pill: MessageId={messageId}, Attempts={retryRecord.AttemptCount}");
                }
            }

            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Registra un procesamiento exitoso
        /// Elimina el registro para no acumular registros innecesarios
        /// </summary>
        /// <param name="messageId">ID único del mensaje</param>
        public async Task RecordSuccess(string messageId)
        {
            var retryRecord = await _db.SyncMessageRetries
                .FirstOrDefaultAsync(r => r.MessageId == messageId);

            if (retryRecord != null)
            {
                // Eliminar registro (o marcarlo como Resolved si prefieres mantener histórico)
                _db.SyncMessageRetries.Remove(retryRecord);
                await _db.SaveChangesAsync();

                Console.WriteLine($"✅ Registro de reintento eliminado: MessageId={messageId}");
            }
        }

        /// <summary>
        /// Registra un fallo en el procesamiento
        /// </summary>
        /// <param name="messageId">ID único del mensaje</param>
        /// <param name="error">Mensaje de error o stack trace</param>
        public async Task RecordFailure(string messageId, string error)
        {
            var retryRecord = await _db.SyncMessageRetries
                .FirstOrDefaultAsync(r => r.MessageId == messageId);

            if (retryRecord != null)
            {
                retryRecord.LastError = TruncateError(error);
                retryRecord.LastAttemptDate = DateTime.UtcNow;

                await _db.SaveChangesAsync();

                Console.WriteLine($"❌ Fallo registrado: MessageId={messageId}, Attempt={retryRecord.AttemptCount}/{MaxAttempts}");
            }
        }

        /// <summary>
        /// Cambia el estado de un mensaje manualmente
        /// Usado por el endpoint de gestión de poison pills
        /// </summary>
        /// <param name="messageId">ID del mensaje</param>
        /// <param name="newStatus">Nuevo estado</param>
        public async Task<bool> ChangeStatus(string messageId, RetryStatus newStatus)
        {
            var retryRecord = await _db.SyncMessageRetries
                .FirstOrDefaultAsync(r => r.MessageId == messageId);

            if (retryRecord == null)
            {
                Console.WriteLine($"⚠️ No se encontró registro para MessageId={messageId}");
                return false;
            }

            var oldStatus = retryRecord.StatusEnum;
            retryRecord.Status = newStatus.ToString();

            // Si se marca como Reprocess, resetear error
            if (newStatus == RetryStatus.Reprocess)
            {
                retryRecord.LastError = null;
            }

            await _db.SaveChangesAsync();

            Console.WriteLine($"🔄 Estado cambiado: MessageId={messageId}, {oldStatus} → {newStatus}");
            return true;
        }

        /// <summary>
        /// Extrae el EntityId del mensaje según la tabla
        /// </summary>
        private string GetEntityId(ExternalSyncMessageDTO message)
        {
            if (message == null) return null;

            // Cada tabla tiene su propio identificador
            if (!string.IsNullOrEmpty(message.Cliente))
                return $"{message.Cliente}-{message.Contacto}";

            if (!string.IsNullOrEmpty(message.Producto))
                return message.Producto;

            return "Unknown";
        }

        /// <summary>
        /// Serializa el mensaje a JSON para almacenar en BD
        /// </summary>
        private string SerializeMessage(ExternalSyncMessageDTO message)
        {
            try
            {
                return JsonSerializer.Serialize(message, new JsonSerializerOptions
                {
                    WriteIndented = false
                });
            }
            catch
            {
                return "Error al serializar mensaje";
            }
        }

        /// <summary>
        /// Trunca el error para que no exceda el límite de la BD
        /// </summary>
        private string TruncateError(string error)
        {
            const int maxLength = 4000; // SQL Server nvarchar(max) tiene buen tamaño, pero por seguridad

            if (string.IsNullOrEmpty(error))
                return null;

            if (error.Length <= maxLength)
                return error;

            return error.Substring(0, maxLength - 3) + "...";
        }
    }
}
