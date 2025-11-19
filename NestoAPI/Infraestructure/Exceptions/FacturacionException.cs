using System;
using System.Net;

namespace NestoAPI.Infraestructure.Exceptions
{
    /// <summary>
    /// Excepción específica para errores en el proceso de facturación.
    ///
    /// EJEMPLOS DE USO:
    ///
    /// // Error simple
    /// throw new FacturacionException(
    ///     "El pedido no tiene líneas para facturar",
    ///     "FACTURACION_SIN_LINEAS",
    ///     empresa: "1",
    ///     pedido: 12345);
    ///
    /// // Con inner exception (wrapping de error de stored procedure)
    /// catch (SqlException ex)
    /// {
    ///     throw new FacturacionException(
    ///         "Error al ejecutar el procedimiento de facturación",
    ///         "FACTURACION_STORED_PROCEDURE_ERROR",
    ///         ex,
    ///         empresa: "1",
    ///         pedido: 12345);
    /// }
    ///
    /// // Con datos adicionales
    /// throw new FacturacionException(
    ///     "La serie de facturación no es válida",
    ///     "FACTURACION_SERIE_INVALIDA",
    ///     empresa: "3",
    ///     pedido: 12345,
    ///     usuario: "carlos")
    ///     .WithData("SerieIntentada", "XX")
    ///     .WithData("SerieEsperada", "NV");
    /// </summary>
    public class FacturacionException : NestoBusinessException
    {
        public FacturacionException(
            string message,
            string errorCode = "FACTURACION_ERROR",
            string empresa = null,
            int? pedido = null,
            string factura = null,
            string usuario = null)
            : base(message, new ErrorContext
            {
                ErrorCode = errorCode,
                Empresa = empresa,
                Pedido = pedido,
                Factura = factura,
                Usuario = usuario
            })
        {
        }

        public FacturacionException(
            string message,
            string errorCode,
            Exception innerException,
            string empresa = null,
            int? pedido = null,
            string factura = null,
            string usuario = null)
            : base(message, new ErrorContext
            {
                ErrorCode = errorCode,
                Empresa = empresa,
                Pedido = pedido,
                Factura = factura,
                Usuario = usuario
            }, innerException)
        {
        }

        /// <summary>
        /// Agrega datos adicionales al contexto del error
        /// </summary>
        public new FacturacionException WithData(string key, object value)
        {
            Context.WithData(key, value);
            return this;
        }

        /// <summary>
        /// Marca esta excepción como warning (no crítica)
        /// </summary>
        public FacturacionException AsWarning()
        {
            IsWarning = true;
            return this;
        }

        /// <summary>
        /// Establece un código de estado HTTP personalizado
        /// </summary>
        public FacturacionException WithStatusCode(HttpStatusCode statusCode)
        {
            StatusCode = statusCode;
            return this;
        }
    }
}
