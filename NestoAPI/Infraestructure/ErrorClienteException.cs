using System;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Excepción que representa un error ocurrido en una aplicación cliente.
    /// Sobrescribe <see cref="StackTrace"/> para exponer la pila capturada en el cliente,
    /// de modo que ELMAH la muestre tal cual (las excepciones sin lanzar tienen StackTrace nulo).
    /// </summary>
    public class ErrorClienteException : Exception
    {
        private readonly string _stackTraceCliente;

        public ErrorClienteException(string mensaje, string stackTraceCliente)
            : base(mensaje)
        {
            _stackTraceCliente = stackTraceCliente;
        }

        public override string StackTrace => _stackTraceCliente;
    }
}
