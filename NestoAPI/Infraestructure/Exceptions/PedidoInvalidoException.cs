using System;
using System.Net;

namespace NestoAPI.Infraestructure.Exceptions
{
    /// <summary>
    /// Excepción para errores de validación de pedidos.
    ///
    /// EJEMPLOS DE USO:
    ///
    /// throw new PedidoInvalidoException(
    ///     "El pedido no tiene líneas",
    ///     "PEDIDO_SIN_LINEAS",
    ///     empresa: "1",
    ///     pedido: 12345,
    ///     cliente: "10458");
    ///
    /// throw new PedidoInvalidoException(
    ///     "El cliente no existe en la empresa destino",
    ///     "PEDIDO_CLIENTE_NO_EXISTE",
    ///     empresa: "3",
    ///     pedido: 12345,
    ///     cliente: "10458")
    ///     .WithData("EmpresaDestino", "3");
    /// </summary>
    public class PedidoInvalidoException : NestoBusinessException
    {
        public PedidoInvalidoException(
            string message,
            string errorCode = "PEDIDO_INVALIDO",
            string empresa = null,
            int? pedido = null,
            string cliente = null,
            string usuario = null)
            : base(message, new ErrorContext
            {
                ErrorCode = errorCode,
                Empresa = empresa,
                Pedido = pedido,
                Cliente = cliente,
                Usuario = usuario
            })
        {
        }

        public PedidoInvalidoException(
            string message,
            string errorCode,
            Exception innerException,
            string empresa = null,
            int? pedido = null,
            string cliente = null,
            string usuario = null)
            : base(message, new ErrorContext
            {
                ErrorCode = errorCode,
                Empresa = empresa,
                Pedido = pedido,
                Cliente = cliente,
                Usuario = usuario
            }, innerException)
        {
        }

        /// <summary>
        /// Agrega datos adicionales al contexto del error
        /// </summary>
        public new PedidoInvalidoException WithData(string key, object value)
        {
            Context.WithData(key, value);
            return this;
        }

        /// <summary>
        /// Marca esta excepción como warning (no crítica)
        /// </summary>
        public PedidoInvalidoException AsWarning()
        {
            IsWarning = true;
            return this;
        }

        /// <summary>
        /// Establece un código de estado HTTP personalizado
        /// </summary>
        public PedidoInvalidoException WithStatusCode(HttpStatusCode statusCode)
        {
            StatusCode = statusCode;
            return this;
        }
    }
}
