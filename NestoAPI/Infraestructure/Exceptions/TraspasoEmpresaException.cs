using System;
using System.Net;

namespace NestoAPI.Infraestructure.Exceptions
{
    /// <summary>
    /// Excepción específica para errores en traspasos de pedidos entre empresas.
    ///
    /// EJEMPLOS DE USO:
    ///
    /// throw new TraspasoEmpresaException(
    ///     "No se pudo copiar el cliente a la empresa destino",
    ///     "TRASPASO_CLIENTE_ERROR",
    ///     empresaOrigen: "1",
    ///     empresaDestino: "3",
    ///     pedido: 12345,
    ///     cliente: "10458");
    ///
    /// catch (SqlException ex)
    /// {
    ///     throw new TraspasoEmpresaException(
    ///         "Error al ejecutar procedimiento de copia de producto",
    ///         "TRASPASO_PRODUCTO_ERROR",
    ///         ex,
    ///         empresaOrigen: "1",
    ///         empresaDestino: "3",
    ///         pedido: 12345)
    ///         .WithData("Producto", "12345678");
    /// }
    /// </summary>
    public class TraspasoEmpresaException : NestoBusinessException
    {
        public TraspasoEmpresaException(
            string message,
            string errorCode = "TRASPASO_ERROR",
            string empresaOrigen = null,
            string empresaDestino = null,
            int? pedido = null,
            string cliente = null,
            string usuario = null)
            : base(message, new ErrorContext
            {
                ErrorCode = errorCode,
                Empresa = empresaOrigen, // La empresa principal es la de origen
                Pedido = pedido,
                Cliente = cliente,
                Usuario = usuario
            })
        {
            if (!string.IsNullOrEmpty(empresaDestino))
            {
                Context.WithData("EmpresaDestino", empresaDestino);
            }
        }

        public TraspasoEmpresaException(
            string message,
            string errorCode,
            Exception innerException,
            string empresaOrigen = null,
            string empresaDestino = null,
            int? pedido = null,
            string cliente = null,
            string usuario = null)
            : base(message, new ErrorContext
            {
                ErrorCode = errorCode,
                Empresa = empresaOrigen,
                Pedido = pedido,
                Cliente = cliente,
                Usuario = usuario
            }, innerException)
        {
            if (!string.IsNullOrEmpty(empresaDestino))
            {
                Context.WithData("EmpresaDestino", empresaDestino);
            }
        }

        /// <summary>
        /// Agrega datos adicionales al contexto del error
        /// </summary>
        public new TraspasoEmpresaException WithData(string key, object value)
        {
            Context.WithData(key, value);
            return this;
        }

        /// <summary>
        /// Marca esta excepción como warning (no crítica)
        /// </summary>
        public TraspasoEmpresaException AsWarning()
        {
            IsWarning = true;
            return this;
        }

        /// <summary>
        /// Establece un código de estado HTTP personalizado
        /// </summary>
        public TraspasoEmpresaException WithStatusCode(HttpStatusCode statusCode)
        {
            StatusCode = statusCode;
            return this;
        }
    }
}
