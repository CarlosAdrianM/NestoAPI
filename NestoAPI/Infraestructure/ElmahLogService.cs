using Elmah;
using System;

namespace NestoAPI.Infraestructure
{
    public class ElmahLogService : ILogService
    {
        public void LogError(string mensaje, Exception excepcionOriginal = null)
        {
            // NestoAPI#182: se delega en ElmahHelper, que envuelve la escritura en un
            // TransactionScope(Suppress). Antes, un LogError disparado dentro de una transacción
            // (facturación, unión de pedidos...) fallaba al abrir la conexión de ELMAH y el error
            // se perdía. El helper conserva el doble camino señal/log-directo y nunca lanza.
            var excepcion = excepcionOriginal != null
                ? new Exception(mensaje, excepcionOriginal)
                : new Exception(mensaje);
            ElmahHelper.Señalar(excepcion);
        }
    }
}
