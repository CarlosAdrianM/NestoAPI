using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Controlador genérico para recibir webhooks de Google Pub/Sub (Push subscription)
    /// Soporta sincronización de múltiples tablas: Clientes, Productos, Proveedores, etc.
    /// </summary>
    [RoutePrefix("api/sync")]
    public class SyncWebhookController : ApiController
    {
        private readonly SyncTableRouter _router;
        private readonly MessageRetryManager _retryManager;
        private static readonly List<string> _recentLogs = new List<string>();
        private static readonly Dictionary<string, DateTime> _recentMessages = new Dictionary<string, DateTime>();
        private static readonly object _lockObj = new object();
        private const int MaxLogs = 100;
        private const int DuplicateDetectionWindowSeconds = 60; // Ventana de 60 segundos para detectar duplicados

        public SyncWebhookController(SyncTableRouter router, MessageRetryManager retryManager = null)
        {
            _router = router;
            _retryManager = retryManager ?? new MessageRetryManager(new Models.NVEntities());
        }

        private void Log(string message)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logEntry = $"[{timestamp}] {message}";

            Console.WriteLine(logEntry);

            lock (_lockObj)
            {
                _recentLogs.Add(logEntry);
                if (_recentLogs.Count > MaxLogs)
                {
                    _recentLogs.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// Deserializa el mensaje JSON al tipo correcto según el campo "Tabla"
        /// </summary>
        private SyncMessageBase DeserializeSyncMessage(string messageJson)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Primero deserializar a un JsonDocument para leer el campo "Tabla"
            using (var document = JsonDocument.Parse(messageJson))
            {
                if (!document.RootElement.TryGetProperty("Tabla", out var tablaElement))
                {
                    Log("⚠️ Mensaje sin campo 'Tabla'");
                    return null;
                }

                string tabla = tablaElement.GetString();

                // Deserializar al tipo correcto según la tabla
                switch (tabla?.ToUpperInvariant())
                {
                    case "CLIENTES":
                        return JsonSerializer.Deserialize<ClienteSyncMessage>(messageJson, options);

                    case "PRODUCTOS":
                        return JsonSerializer.Deserialize<ProductoSyncMessage>(messageJson, options);

                    default:
                        Log($"⚠️ Tabla desconocida: {tabla}");
                        return null;
                }
            }
        }

        /// <summary>
        /// Endpoint que recibe mensajes push de Google Pub/Sub
        /// URL: POST /api/sync/webhook
        /// </summary>
        /// <param name="request">Request de Pub/Sub con mensaje en base64</param>
        /// <returns>200 OK si procesó exitosamente, 400/500 en caso de error</returns>
        [HttpPost]
        [Route("webhook")]
        [AllowAnonymous] // Google Pub/Sub hace POST sin autenticación (usar IP allowlist en producción)
        public async Task<IHttpActionResult> ReceiveWebhook([FromBody] PubSubPushRequestDTO request)
        {
            try
            {
                // Validar request
                if (request?.Message?.Data == null)
                {
                    Log("⚠️ Request inválido: mensaje vacío");
                    return BadRequest("Mensaje vacío o formato incorrecto");
                }

                Log($"📨 Webhook recibido: MessageId={request.Message.MessageId}, Subscription={request.Subscription}");

                // Decodificar datos de base64
                string messageJson;
                try
                {
                    byte[] data = Convert.FromBase64String(request.Message.Data);
                    messageJson = Encoding.UTF8.GetString(data);
                }
                catch (FormatException ex)
                {
                    Log($"❌ Error decodificando base64: {ex.Message}");
                    return BadRequest("Error decodificando mensaje base64");
                }

                // Deserializar mensaje al tipo correcto según la tabla
                SyncMessageBase syncMessage;
                try
                {
                    syncMessage = DeserializeSyncMessage(messageJson);

                    if (syncMessage == null)
                    {
                        Log($"⚠️ Error deserializando mensaje: tipo no reconocido");
                        return BadRequest("Tipo de mensaje no reconocido");
                    }

                    // Obtener el handler apropiado para este mensaje
                    var handler = _router.GetHandler(syncMessage);

                    if (handler == null)
                    {
                        Log($"⚠️ No hay handler para Tabla={syncMessage?.Tabla}. Handlers disponibles: {string.Join(", ", _router.GetSupportedTables())}");
                        return BadRequest($"Tabla '{syncMessage?.Tabla}' no soportada");
                    }

                    // El handler genera la clave y el log info (cada uno sabe su lógica)
                    string messageKey = handler.GetMessageKey(syncMessage);
                    string logInfo = $"MessageId={request.Message.MessageId} - {handler.GetLogInfo(syncMessage)}";

                    // Detectar duplicados
                    lock (_lockObj)
                    {
                        // Limpiar mensajes antiguos (fuera de la ventana de detección)
                        var cutoffTime = DateTime.UtcNow.AddSeconds(-DuplicateDetectionWindowSeconds);
                        var keysToRemove = _recentMessages.Where(kvp => kvp.Value < cutoffTime).Select(kvp => kvp.Key).ToList();
                        foreach (var key in keysToRemove)
                        {
                            _recentMessages.Remove(key);
                        }

                        // Verificar si es duplicado
                        if (_recentMessages.ContainsKey(messageKey))
                        {
                            var timeSinceLastMessage = DateTime.UtcNow - _recentMessages[messageKey];
                            logInfo += $" ⚠️ POSIBLE DUPLICADO (último mensaje hace {timeSinceLastMessage.TotalSeconds:F1}s)";
                        }

                        // Registrar este mensaje
                        _recentMessages[messageKey] = DateTime.UtcNow;
                    }

                    Log($"📄 {logInfo}");
                }
                catch (JsonException ex)
                {
                    Log($"❌ Error deserializando JSON: {ex.Message}");
                    return BadRequest($"Error deserializando mensaje: {ex.Message}");
                }

                // ========== CONTROL DE REINTENTOS ==========
                // Verificar si el mensaje debe procesarse o es poison pill
                string messageId = request.Message.MessageId;
                bool shouldProcess = await _retryManager.ShouldProcessMessage(messageId);

                if (!shouldProcess)
                {
                    Log($"🚫 Mensaje descartado (poison pill o fallo permanente): {messageId}");
                    // Retornar 200 para que Pub/Sub no reenvíe
                    return Ok(new
                    {
                        success = false,
                        messageId,
                        message = "Mensaje descartado (poison pill o fallo permanente)",
                        poisonPill = true
                    });
                }

                // Registrar intento de procesamiento
                await _retryManager.RecordAttempt(messageId, syncMessage);

                // Rutear al handler correcto
                bool success = await _router.RouteAsync(syncMessage);

                if (success)
                {
                    Log($"✅ Mensaje procesado exitosamente: {messageId}");

                    // Registrar éxito (elimina el registro de reintentos)
                    await _retryManager.RecordSuccess(messageId);

                    return Ok(new
                    {
                        success = true,
                        messageId,
                        tabla = syncMessage?.Tabla,
                        source = syncMessage?.Source
                    });
                }
                else
                {
                    Log($"⚠️ Mensaje procesado con advertencias: {messageId}");

                    // Registrar fallo
                    await _retryManager.RecordFailure(messageId, "Procesado con advertencias (ver logs)");

                    // Retornar 200 para que Pub/Sub no reenvíe (el error fue lógico, no técnico)
                    return Ok(new
                    {
                        success = false,
                        messageId,
                        message = "Procesado con advertencias (ver logs)"
                    });
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error crítico procesando webhook: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");

                // Registrar el fallo
                string messageId = request?.Message?.MessageId ?? "unknown";
                await _retryManager.RecordFailure(messageId, $"{ex.Message}\n{ex.StackTrace}");

                // Verificar si ya alcanzó el límite de reintentos
                bool shouldRetry = await _retryManager.ShouldProcessMessage(messageId);

                if (!shouldRetry)
                {
                    Log($"☠️ Mensaje alcanzó límite de reintentos, marcado como poison pill: {messageId}");
                    // Retornar 200 para que Pub/Sub NO reenvíe (ya es poison pill)
                    return Ok(new
                    {
                        success = false,
                        messageId,
                        message = "Error crítico - límite de reintentos alcanzado",
                        error = ex.Message,
                        poisonPill = true
                    });
                }

                // Aún dentro del límite, retornar 500 para que Pub/Sub reenvíe
                Log($"🔄 Retornando 500 para reintento (Pub/Sub reenviará): {messageId}");
                return InternalServerError(ex);
            }
        }

        /// <summary>
        /// Endpoint de health check para verificar que el webhook está activo
        /// URL: GET /api/sync/health
        /// </summary>
        [HttpGet]
        [Route("health")]
        [AllowAnonymous]
        public IHttpActionResult Health()
        {
            var supportedTables = _router.GetSupportedTables();

            return Ok(new
            {
                status = "healthy",
                service = "SyncWebhook",
                supportedTables,
                timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Endpoint para consultar los últimos logs del webhook
        /// URL: GET /api/sync/logs
        /// </summary>
        [HttpGet]
        [Route("logs")]
        [AllowAnonymous]
        public IHttpActionResult GetLogs()
        {
            List<string> logs;
            lock (_lockObj)
            {
                logs = new List<string>(_recentLogs);
            }

            return Ok(new
            {
                totalLogs = logs.Count,
                logs,
                timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Endpoint para listar poison pills (mensajes que fallaron repetidamente)
        /// URL: GET /api/sync/poisonpills?status=PoisonPill
        /// </summary>
        /// <param name="status">Filtro opcional por estado: PoisonPill, Retrying, Reprocess, Resolved, PermanentFailure</param>
        /// <param name="tabla">Filtro opcional por tabla: Clientes, Productos, etc.</param>
        /// <param name="limit">Número máximo de registros a retornar (default: 100)</param>
        [HttpGet]
        [Route("poisonpills")]
        [AllowAnonymous]
        public async Task<IHttpActionResult> GetPoisonPills(string status = null, string tabla = null, int limit = 100)
        {
            try
            {
                using (var db = new Models.NVEntities())
                {
                    var query = db.SyncMessageRetries.AsQueryable();

                    // Filtrar por estado si se especifica
                    if (!string.IsNullOrEmpty(status))
                    {
                        query = query.Where(r => r.Status == status);
                    }

                    // Filtrar por tabla si se especifica
                    if (!string.IsNullOrEmpty(tabla))
                    {
                        query = query.Where(r => r.Tabla == tabla);
                    }

                    // Ordenar por último intento (más reciente primero)
                    var records = await query
                        .OrderByDescending(r => r.LastAttemptDate)
                        .Take(limit)
                        .ToListAsync();

                    var now = DateTime.UtcNow;

                    // Convertir a DTOs
                    var dtos = records.Select(r => new PoisonPillDTO
                    {
                        MessageId = r.MessageId,
                        Tabla = r.Tabla,
                        EntityId = r.EntityId,
                        Source = r.Source,
                        AttemptCount = r.AttemptCount,
                        FirstAttemptDate = r.FirstAttemptDate,
                        LastAttemptDate = r.LastAttemptDate,
                        LastError = r.LastError,
                        Status = r.Status,
                        MessageData = r.MessageData,
                        TimeSinceFirstAttempt = FormatTimeSpan(now - r.FirstAttemptDate),
                        TimeSinceLastAttempt = FormatTimeSpan(now - r.LastAttemptDate)
                    }).ToList();

                    return Ok(new
                    {
                        total = dtos.Count,
                        filters = new { status, tabla, limit },
                        poisonPills = dtos,
                        timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error obteniendo poison pills: {ex.Message}");
                return InternalServerError(ex);
            }
        }

        /// <summary>
        /// Endpoint para cambiar el estado de un mensaje de sincronización
        /// URL: POST /api/sync/poisonpills/changestatus
        /// </summary>
        /// <param name="request">Request con MessageId y NewStatus</param>
        [HttpPost]
        [Route("poisonpills/changestatus")]
        [AllowAnonymous]
        public async Task<IHttpActionResult> ChangeStatus([FromBody] ChangeStatusRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrEmpty(request.MessageId) || string.IsNullOrEmpty(request.NewStatus))
                {
                    return BadRequest("MessageId y NewStatus son requeridos");
                }

                // Validar que el nuevo estado sea válido
                if (!Enum.TryParse<RetryStatus>(request.NewStatus, out var newStatus))
                {
                    return BadRequest($"Estado inválido: {request.NewStatus}. Valores permitidos: Reprocess, Resolved, PermanentFailure");
                }

                // Validar que solo se permitan estados manuales
                if (newStatus == RetryStatus.Retrying || newStatus == RetryStatus.PoisonPill)
                {
                    return BadRequest($"No se puede cambiar manualmente a estado: {newStatus}");
                }

                bool success = await _retryManager.ChangeStatus(request.MessageId, newStatus);

                if (!success)
                {
                    return NotFound();
                }

                Log($"🔄 Estado cambiado: MessageId={request.MessageId}, NewStatus={request.NewStatus}");

                return Ok(new
                {
                    success = true,
                    messageId = request.MessageId,
                    newStatus = request.NewStatus,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                Log($"❌ Error cambiando estado: {ex.Message}");
                return InternalServerError(ex);
            }
        }

        /// <summary>
        /// Formatea un TimeSpan en un string legible
        /// </summary>
        private string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalDays >= 1)
                return $"{ts.Days}d {ts.Hours}h";
            if (ts.TotalHours >= 1)
                return $"{ts.Hours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes}m {ts.Seconds}s";
            return $"{ts.Seconds}s";
        }
    }
}
