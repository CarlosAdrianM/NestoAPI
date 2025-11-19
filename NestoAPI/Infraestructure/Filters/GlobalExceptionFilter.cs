using Elmah;
using NestoAPI.Infraestructure.Exceptions;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Web.Http.Filters;

namespace NestoAPI.Infraestructure.Filters
{
    /// <summary>
    /// Filtro global que captura todas las excepciones no manejadas y las formatea
    /// en respuestas HTTP consistentes con información útil para debugging.
    ///
    /// COMPORTAMIENTO:
    /// - Excepciones NestoBusinessException → Respuesta estructurada con contexto completo
    /// - Otras excepciones → Respuesta genérica con mensaje de error
    /// - En modo DEBUG: Incluye stack trace
    /// - En modo RELEASE: Oculta detalles técnicos sensibles
    ///
    /// FORMATO DE RESPUESTA:
    /// {
    ///   "error": {
    ///     "code": "FACTURACION_IVA_FALTANTE",
    ///     "message": "El pedido 12345 no se puede facturar...",
    ///     "details": {
    ///       "empresa": "1",
    ///       "pedido": 12345,
    ///       "usuario": "carlos"
    ///     },
    ///     "timestamp": "2025-01-19T10:30:00Z",
    ///     "stackTrace": "..." // Solo en DEBUG
    ///   }
    /// }
    /// </summary>
    public class GlobalExceptionFilter : ExceptionFilterAttribute
    {
        public override void OnException(HttpActionExecutedContext context)
        {
            var exception = context.Exception;

            // Logging con Elmah (persiste en base de datos)
            try
            {
                ErrorSignal.FromCurrentContext().Raise(exception);
            }
            catch
            {
                // Si falla Elmah, continuar de todas formas
                // Fallback a System.Diagnostics
            }

            // Logging adicional en consola (útil para debugging)
            LogException(exception);

            HttpStatusCode statusCode;
            object responseContent;

            // Manejo especial para NestoBusinessException
            if (exception is NestoBusinessException businessException)
            {
                statusCode = businessException.StatusCode;
                responseContent = CreateBusinessErrorResponse(businessException);
            }
            else
            {
                // Excepciones genéricas
                statusCode = HttpStatusCode.InternalServerError;
                responseContent = CreateGenericErrorResponse(exception);
            }

            context.Response = context.Request.CreateResponse(statusCode, responseContent);
        }

        /// <summary>
        /// Crea una respuesta estructurada para excepciones de negocio
        /// </summary>
        private object CreateBusinessErrorResponse(NestoBusinessException exception)
        {
            var errorResponse = new Dictionary<string, object>
            {
                ["error"] = new Dictionary<string, object>
                {
                    ["code"] = exception.GetErrorCode(),
                    ["message"] = exception.Message,
                    ["timestamp"] = exception.Context?.Timestamp ?? DateTime.Now
                }
            };

            var errorDict = (Dictionary<string, object>)errorResponse["error"];

            // Agregar contexto si está disponible
            if (exception.Context != null)
            {
                var details = new Dictionary<string, object>();

                if (!string.IsNullOrEmpty(exception.Context.Empresa))
                    details["empresa"] = exception.Context.Empresa;

                if (exception.Context.Pedido.HasValue)
                    details["pedido"] = exception.Context.Pedido.Value;

                if (!string.IsNullOrEmpty(exception.Context.Cliente))
                    details["cliente"] = exception.Context.Cliente;

                if (!string.IsNullOrEmpty(exception.Context.Usuario))
                    details["usuario"] = exception.Context.Usuario;

                if (!string.IsNullOrEmpty(exception.Context.Factura))
                    details["factura"] = exception.Context.Factura;

                // Agregar datos adicionales
                if (exception.Context.AdditionalData != null && exception.Context.AdditionalData.Count > 0)
                {
                    foreach (var kvp in exception.Context.AdditionalData)
                    {
                        details[kvp.Key] = kvp.Value;
                    }
                }

                if (details.Count > 0)
                {
                    errorDict["details"] = details;
                }
            }

#if DEBUG
            // En modo DEBUG, incluir stack trace y inner exception
            errorDict["stackTrace"] = exception.StackTrace;

            if (exception.InnerException != null)
            {
                errorDict["innerException"] = new Dictionary<string, object>
                {
                    ["message"] = exception.InnerException.Message,
                    ["type"] = exception.InnerException.GetType().Name,
                    ["stackTrace"] = exception.InnerException.StackTrace
                };
            }
#endif

            return errorResponse;
        }

        /// <summary>
        /// Crea una respuesta genérica para excepciones no controladas
        /// </summary>
        private object CreateGenericErrorResponse(Exception exception)
        {
            var errorResponse = new Dictionary<string, object>
            {
                ["error"] = new Dictionary<string, object>
                {
                    ["code"] = "INTERNAL_ERROR",
                    ["message"] = exception.Message,
                    ["timestamp"] = DateTime.Now
                }
            };

#if DEBUG
            // En modo DEBUG, incluir información técnica detallada
            var errorDict = (Dictionary<string, object>)errorResponse["error"];
            errorDict["type"] = exception.GetType().Name;
            errorDict["stackTrace"] = exception.StackTrace;

            if (exception.InnerException != null)
            {
                errorDict["innerException"] = new Dictionary<string, object>
                {
                    ["message"] = exception.InnerException.Message,
                    ["type"] = exception.InnerException.GetType().Name,
                    ["stackTrace"] = exception.InnerException.StackTrace
                };
            }
#else
            // En producción, mensaje genérico para evitar exponer detalles internos
            var errorDict = (Dictionary<string, object>)errorResponse["error"];
            errorDict["message"] = "Ha ocurrido un error interno. Por favor, contacte con el administrador.";
#endif

            return errorResponse;
        }

        /// <summary>
        /// Logging de excepciones
        /// TODO: Reemplazar con un sistema de logging más sofisticado (Serilog, NLog, etc.)
        /// </summary>
        private void LogException(Exception exception)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

            if (exception is NestoBusinessException businessException)
            {
                var logLevel = businessException.IsWarning ? "WARN" : "ERROR";
                var fullMessage = businessException.GetFullMessage();

                System.Diagnostics.Debug.WriteLine($"[{timestamp}] [{logLevel}] {fullMessage}");

                if (businessException.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[{timestamp}] [ERROR] Inner Exception: {businessException.InnerException.Message}");
                    System.Diagnostics.Debug.WriteLine($"[{timestamp}] [ERROR] Stack Trace: {businessException.InnerException.StackTrace}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[{timestamp}] [ERROR] Unhandled Exception: {exception.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"[{timestamp}] [ERROR] Message: {exception.Message}");
                System.Diagnostics.Debug.WriteLine($"[{timestamp}] [ERROR] Stack Trace: {exception.StackTrace}");

                if (exception.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[{timestamp}] [ERROR] Inner Exception: {exception.InnerException.Message}");
                }
            }
        }
    }
}
