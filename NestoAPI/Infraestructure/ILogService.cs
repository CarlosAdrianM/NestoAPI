using System;

namespace NestoAPI.Infraestructure
{
    public interface ILogService
    {
        void LogError(string mensaje, Exception excepcionOriginal = null);
    }
}
