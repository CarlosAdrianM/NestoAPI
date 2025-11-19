using System;
using System.Net;

namespace NestoAPI.Infraestructure.Exceptions
{
    /// <summary>
    /// Excepción base para todos los errores de negocio de Nesto.
    /// Incluye contexto rico para debugging y respuestas HTTP apropiadas.
    ///
    /// EJEMPLOS DE USO:
    ///
    /// throw new NestoBusinessException(
    ///     "El pedido no se puede facturar porque falta el campo IVA",
    ///     new ErrorContext
    ///     {
    ///         ErrorCode = "FACTURACION_IVA_FALTANTE",
    ///         Empresa = "1",
    ///         Pedido = 12345,
    ///         Usuario = "carlos"
    ///     });
    ///
    /// O usando excepciones específicas (recomendado):
    ///
    /// throw new FacturacionException(
    ///     "El pedido no se puede facturar porque falta el campo IVA",
    ///     "FACTURACION_IVA_FALTANTE",
    ///     empresa: "1",
    ///     pedido: 12345,
    ///     usuario: "carlos");
    /// </summary>
    public class NestoBusinessException : Exception
    {
        /// <summary>
        /// Contexto del error con información adicional
        /// </summary>
        public ErrorContext Context { get; set; }

        /// <summary>
        /// Código de estado HTTP sugerido para la respuesta (default: 400 BadRequest)
        /// </summary>
        public HttpStatusCode StatusCode { get; set; }

        /// <summary>
        /// Indica si el error debe ser loggeado como error o como warning
        /// (algunos errores de validación son esperados y no críticos)
        /// </summary>
        public bool IsWarning { get; set; }

        public NestoBusinessException(string message)
            : base(message)
        {
            Context = new ErrorContext();
            StatusCode = HttpStatusCode.BadRequest;
            IsWarning = false;
        }

        public NestoBusinessException(string message, ErrorContext context)
            : base(message)
        {
            Context = context ?? new ErrorContext();
            StatusCode = HttpStatusCode.BadRequest;
            IsWarning = false;
        }

        public NestoBusinessException(string message, ErrorContext context, Exception innerException)
            : base(message, innerException)
        {
            Context = context ?? new ErrorContext();
            StatusCode = HttpStatusCode.BadRequest;
            IsWarning = false;
        }

        public NestoBusinessException(string message, Exception innerException)
            : base(message, innerException)
        {
            Context = new ErrorContext();
            StatusCode = HttpStatusCode.BadRequest;
            IsWarning = false;
        }

        /// <summary>
        /// Obtiene el mensaje completo del error incluyendo el contexto
        /// </summary>
        public string GetFullMessage()
        {
            if (Context == null || string.IsNullOrEmpty(Context.ToString()))
            {
                return Message;
            }

            return $"{Message} [{Context}]";
        }

        /// <summary>
        /// Obtiene el código de error o genera uno por defecto
        /// </summary>
        public string GetErrorCode()
        {
            return Context?.ErrorCode ?? "BUSINESS_ERROR";
        }
    }
}
