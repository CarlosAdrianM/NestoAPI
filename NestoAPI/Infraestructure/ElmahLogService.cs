using Elmah;
using System;

namespace NestoAPI.Infraestructure
{
    public class ElmahLogService : ILogService
    {
        public void LogError(string mensaje, Exception excepcionOriginal = null)
        {
            try
            {
                var excepcion = excepcionOriginal != null
                    ? new Exception(mensaje, excepcionOriginal)
                    : new Exception(mensaje);
                ErrorSignal.FromCurrentContext().Raise(excepcion);
            }
            catch
            {
                try
                {
                    var excepcion = excepcionOriginal != null
                        ? new Exception(mensaje, excepcionOriginal)
                        : new Exception(mensaje);
                    ErrorLog.GetDefault(null).Log(new Error(excepcion));
                }
                catch
                {
                    // No bloquear si falla el logging
                }
            }
        }
    }
}
