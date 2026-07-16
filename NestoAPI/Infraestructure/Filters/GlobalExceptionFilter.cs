using Elmah;
using NestoAPI.Infraestructure.Exceptions;
using System;
using System.Collections.Generic;
using System.Data.Entity.Validation;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
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
                RegistrarEnElmah(exception);
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
            // Manejo especial para DbEntityValidationException (errores de validación de Entity Framework)
            else if (exception is DbEntityValidationException validationException)
            {
                statusCode = HttpStatusCode.BadRequest;
                responseContent = CreateValidationErrorResponse(validationException);
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
        /// Registra la excepción en ELMAH. Si es una <see cref="NestoBusinessException"/> con datos
        /// de contexto (p. ej. el JSON del pedido que falló la validación, NestoAPI#215), construye
        /// el <see cref="Elmah.Error"/> a mano y vuelca cada dato de <c>AdditionalData</c> a una
        /// ServerVariable (<c>X-Context-*</c>), para verlo en la misma ficha de ELMAH y poder
        /// reproducir el caso. <c>ErrorSignal.Raise</c> no permite inyectar ServerVariables, por eso
        /// se usa <c>ErrorLog.Log</c> directamente en ese caso; el resto sigue por ErrorSignal (que
        /// aplica el errorFilter de Web.config, p. ej. el de OperationCanceledException).
        /// </summary>
        private void RegistrarEnElmah(Exception exception)
        {
            var contexto = (exception as NestoBusinessException)?.Context;
            var httpContext = System.Web.HttpContext.Current;

            if (httpContext != null && contexto?.AdditionalData != null && contexto.AdditionalData.Count > 0)
            {
                var error = new Elmah.Error(exception, httpContext);
                VolcarContextoAServerVariables(error.ServerVariables, contexto);
                _ = Elmah.ErrorLog.GetDefault(httpContext).Log(error);
            }
            // NestoAPI#309: "Validation failed for one or more entities" sin más era imposible de
            // diagnosticar a posteriori (ELMAH no muestra la propiedad EntityValidationErrors).
            // Se vuelca el detalle (entidad, propiedad, mensaje) a una ServerVariable X-Context-*
            // de la ficha de ELMAH, igual que el contexto de NestoBusinessException.
            else if (httpContext != null && BuscarValidationException(exception) is DbEntityValidationException validationException)
            {
                var error = new Elmah.Error(exception, httpContext);
                error.ServerVariables.Add("X-Context-EntityValidationErrors", ResumenErroresValidacion(validationException));
                _ = Elmah.ErrorLog.GetDefault(httpContext).Log(error);
            }
            else
            {
                ErrorSignal.FromCurrentContext().Raise(exception);
            }
        }

        /// <summary>
        /// NestoAPI#309: localiza una DbEntityValidationException en la propia excepción o en su
        /// cadena de InnerException (hay controllers que la envuelven en excepciones genéricas).
        /// </summary>
        internal static DbEntityValidationException BuscarValidationException(Exception exception)
        {
            for (Exception actual = exception; actual != null; actual = actual.InnerException)
            {
                if (actual is DbEntityValidationException validationException)
                {
                    return validationException;
                }
            }
            return null;
        }

        /// <summary>
        /// NestoAPI#309: texto legible con entidad, estado, propiedad y mensaje de cada error de
        /// validación de EF. Compartido entre la respuesta HTTP y la ficha de ELMAH.
        /// </summary>
        internal static string ResumenErroresValidacion(DbEntityValidationException exception)
        {
            var messageBuilder = new StringBuilder();
            _ = messageBuilder.AppendLine("Error de validación de Entity Framework:");
            foreach (var entityError in exception.EntityValidationErrors)
            {
                var entityName = entityError.Entry.Entity.GetType().Name;
                _ = messageBuilder.AppendLine($"  Entidad: {entityName} ({entityError.Entry.State})");
                foreach (var propertyError in entityError.ValidationErrors)
                {
                    _ = messageBuilder.AppendLine($"    - {propertyError.PropertyName}: {propertyError.ErrorMessage}");
                }
            }
            return messageBuilder.ToString();
        }

        /// <summary>
        /// Copia cada entrada de <c>AdditionalData</c> del contexto a una ServerVariable del Error de
        /// ELMAH con prefijo <c>X-Context-</c>. Los valores no string se serializan a JSON. Así el JSON
        /// del pedido (NestoAPI#215), los Motivos y los Errores quedan en la ficha del error.
        /// </summary>
        internal static void VolcarContextoAServerVariables(System.Collections.Specialized.NameValueCollection serverVariables, ErrorContext contexto)
        {
            if (serverVariables == null || contexto?.AdditionalData == null)
            {
                return;
            }

            foreach (var kvp in contexto.AdditionalData)
            {
                if (kvp.Value == null)
                {
                    continue;
                }

                string valor = kvp.Value as string
                    ?? Newtonsoft.Json.JsonConvert.SerializeObject(kvp.Value);
                serverVariables.Add("X-Context-" + kvp.Key, valor);
            }
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
        /// Crea una respuesta estructurada para errores de validación de Entity Framework
        /// </summary>
        private object CreateValidationErrorResponse(DbEntityValidationException exception)
        {
            var validationErrors = new List<object>();

            foreach (var entityError in exception.EntityValidationErrors)
            {
                var entityName = entityError.Entry.Entity.GetType().Name;
                var entityState = entityError.Entry.State.ToString();

                foreach (var propertyError in entityError.ValidationErrors)
                {
                    validationErrors.Add(new Dictionary<string, object>
                    {
                        ["entity"] = entityName,
                        ["state"] = entityState,
                        ["property"] = propertyError.PropertyName,
                        ["error"] = propertyError.ErrorMessage
                    });
                }
            }

            var errorResponse = new Dictionary<string, object>
            {
                ["error"] = new Dictionary<string, object>
                {
                    ["code"] = "ENTITY_VALIDATION_ERROR",
                    ["message"] = ResumenErroresValidacion(exception),
                    ["timestamp"] = DateTime.Now,
                    ["validationErrors"] = validationErrors
                }
            };

#if DEBUG
            var errorDict = (Dictionary<string, object>)errorResponse["error"];
            errorDict["stackTrace"] = exception.StackTrace;
#endif

            return errorResponse;
        }

        /// <summary>
        /// Crea una respuesta genérica para excepciones no controladas.
        ///
        /// IMPORTANTE: El mensaje de error siempre se muestra al usuario (tanto en DEBUG como en RELEASE)
        /// porque los errores operativos (como "ubicación descuadrada") necesitan ser visibles
        /// para que el usuario pueda tomar acción. Solo se ocultan los detalles técnicos
        /// (stack trace, inner exception) en modo RELEASE.
        /// </summary>
        private object CreateGenericErrorResponse(Exception exception)
        {
            var errorResponse = new Dictionary<string, object>
            {
                ["error"] = new Dictionary<string, object>
                {
                    ["code"] = "INTERNAL_ERROR",
                    ["message"] = exception.Message, // Siempre mostrar el mensaje real
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
#endif
            // En RELEASE: se muestra el mensaje pero se ocultan los detalles técnicos
            // (stack trace, inner exception, type) por seguridad

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
            else if (exception is DbEntityValidationException validationException)
            {
                System.Diagnostics.Debug.WriteLine($"[{timestamp}] [ERROR] Entity Validation Exception:");
                foreach (var entityError in validationException.EntityValidationErrors)
                {
                    var entityName = entityError.Entry.Entity.GetType().Name;
                    System.Diagnostics.Debug.WriteLine($"[{timestamp}] [ERROR]   Entidad: {entityName} ({entityError.Entry.State})");
                    foreach (var propertyError in entityError.ValidationErrors)
                    {
                        System.Diagnostics.Debug.WriteLine($"[{timestamp}] [ERROR]     - {propertyError.PropertyName}: {propertyError.ErrorMessage}");
                    }
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
