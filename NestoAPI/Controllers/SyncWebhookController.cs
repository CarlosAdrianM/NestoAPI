using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models.Sincronizacion;
using System;
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

        public SyncWebhookController(SyncTableRouter router)
        {
            _router = router;
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
                    Console.WriteLine("⚠️ Request inválido: mensaje vacío");
                    return BadRequest("Mensaje vacío o formato incorrecto");
                }

                Console.WriteLine($"📨 Webhook recibido: MessageId={request.Message.MessageId}, Subscription={request.Subscription}");

                // Decodificar datos de base64
                string messageJson;
                try
                {
                    byte[] data = Convert.FromBase64String(request.Message.Data);
                    messageJson = Encoding.UTF8.GetString(data);
                    Console.WriteLine($"📄 Mensaje decodificado: {messageJson.Substring(0, Math.Min(200, messageJson.Length))}...");
                }
                catch (FormatException ex)
                {
                    Console.WriteLine($"❌ Error decodificando base64: {ex.Message}");
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
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"❌ Error deserializando JSON: {ex.Message}");
                    return BadRequest($"Error deserializando mensaje: {ex.Message}");
                }

                // Rutear al handler correcto
                bool success = await _router.RouteAsync(syncMessage);

                if (success)
                {
                    Console.WriteLine($"✅ Mensaje procesado exitosamente: {request.Message.MessageId}");
                    return Ok(new {
                        success = true,
                        messageId = request.Message.MessageId,
                        tabla = syncMessage?.Tabla,
                        source = syncMessage?.Source
                    });
                }
                else
                {
                    Console.WriteLine($"⚠️ Mensaje procesado con advertencias: {request.Message.MessageId}");
                    // Retornar 200 para que Pub/Sub no reenvíe (el error fue lógico, no técnico)
                    return Ok(new {
                        success = false,
                        messageId = request.Message.MessageId,
                        message = "Procesado con advertencias (ver logs)"
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error crítico procesando webhook: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

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
                supportedTables = supportedTables,
                timestamp = DateTime.UtcNow
            });
        }
    }
}
