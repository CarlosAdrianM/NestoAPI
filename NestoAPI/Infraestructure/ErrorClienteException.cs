using System;
using System.Reflection;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Excepción que representa un error ocurrido en una aplicación cliente.
    /// Sobrescribe <see cref="StackTrace"/> para exponer la pila capturada en el cliente,
    /// de modo que ELMAH la muestre tal cual (las excepciones sin lanzar tienen StackTrace nulo).
    /// </summary>
    public class ErrorClienteException : Exception
    {
        // En .NET Framework, ni Exception.ToString() ni el ToString() del wrapper que crea
        // ElmahLogService usan la propiedad virtual StackTrace: leen el campo interno vía
        // GetStackTrace(). Por eso el override de StackTrace no llegaba al Detail de ELMAH y los
        // crashes de los clientes se registraban SIN pila, imposibles de localizar (Nesto#377).
        // El campo _remoteStackTraceString es el mecanismo del propio runtime para pilas remotas
        // (lo antepone en todas esas rutas internas), así que rellenarlo hace que la pila del
        // cliente aparezca también en el detalle, incluso envuelta en otra excepción.
        private static readonly FieldInfo _remoteStackTrace =
            typeof(Exception).GetField("_remoteStackTraceString", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly string _stackTraceCliente;

        public ErrorClienteException(string mensaje, string stackTraceCliente)
            : base(mensaje)
        {
            _stackTraceCliente = stackTraceCliente;
            if (!string.IsNullOrWhiteSpace(stackTraceCliente))
            {
                _remoteStackTrace?.SetValue(this, stackTraceCliente + Environment.NewLine);
            }
        }

        public override string StackTrace => _stackTraceCliente;
    }
}
