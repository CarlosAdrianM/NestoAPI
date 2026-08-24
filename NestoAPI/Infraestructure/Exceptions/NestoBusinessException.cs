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

        /// <summary>
        /// NestoAPI#361: si esta excepción de negocio debe registrarse en ELMAH.
        ///
        /// Por defecto NO. Una excepción de negocio es una respuesta ESPERADA (un 400 que el
        /// cliente sabe interpretar), no un fallo del sistema: llenar ELMAH con ellas tapa los
        /// errores de verdad. Es el mismo criterio de #336 pero para negocio en vez de bots.
        ///
        /// Se pone a <c>true</c> en el sitio que la lanza cuando ese caso concreto SÍ se quiere
        /// vigilar (p. ej. porque adjunta contexto para reproducirlo después). Una excepción de
        /// negocio con StatusCode 5xx se registra siempre, marque lo que marque esta propiedad:
        /// un 5xx significa que algo se rompió de verdad.
        /// </summary>
        public bool RegistrarEnLog { get; set; }

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
