using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Collections.Generic;
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
        private static readonly List<string> _recentLogs = new List<string>();
        private static readonly Dictionary<string, DateTime> _recentMessages = new Dictionary<string, DateTime>();
        private static readonly object _lockObj = new object();
        private const int MaxLogs = 100;
        private const int DuplicateDetectionWindowSeconds = 60; // Ventana de 60 segundos para detectar duplicados

        public SyncWebhookController(SyncTableRouter router)
        {
            _router = router;
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

                // Deserializar mensaje
                ExternalSyncMessageDTO syncMessage;
                try
                {
                    syncMessage = JsonSerializer.Deserialize<ExternalSyncMessageDTO>(messageJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    // Loguear información detallada del mensaje
                    string logInfo = $"MessageId={request.Message.MessageId}";

                    if (!string.IsNullOrEmpty(syncMessage?.Cliente))
                    {
                        logInfo += $" - Cliente {syncMessage.Cliente}";
                    }

                    if (!string.IsNullOrEmpty(syncMessage?.Contacto))
                    {
                        logInfo += $", Contacto {syncMessage.Contacto}";
                    }

                    if (!string.IsNullOrEmpty(syncMessage?.Source))
                    {
                        logInfo += $", Source={syncMessage.Source}";
                    }

                    if (syncMessage?.PersonasContacto != null && syncMessage.PersonasContacto.Count > 0)
                    {
                        var personasInfo = string.Join(", ", syncMessage.PersonasContacto.Select(p =>
                            $"Id={p.Id} ({p.Nombre})"
                        ));
                        logInfo += $", PersonasContacto=[{personasInfo}]";
                    }

                    // Detectar duplicados
                    string messageKey = $"{syncMessage?.Cliente}|{syncMessage?.Contacto}|{syncMessage?.Source}";
                    bool isDuplicate = false;

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
                            isDuplicate = true;
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

                // Rutear al handler correcto
                bool success = await _router.RouteAsync(syncMessage);

                if (success)
                {
                    Log($"✅ Mensaje procesado exitosamente: {request.Message.MessageId}");
                    return Ok(new
                    {
                        success = true,
                        messageId = request.Message.MessageId,
                        tabla = syncMessage?.Tabla,
                        source = syncMessage?.Source
                    });
                }
                else
                {
                    Log($"⚠️ Mensaje procesado con advertencias: {request.Message.MessageId}");
                    // Retornar 200 para que Pub/Sub no reenvíe (el error fue lógico, no técnico)
                    return Ok(new
                    {
                        success = false,
                        messageId = request.Message.MessageId,
                        message = "Procesado con advertencias (ver logs)"
                    });
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error crítico procesando webhook: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");

                // Retornar 500 para que Pub/Sub reenvíe el mensaje
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
    }
}
