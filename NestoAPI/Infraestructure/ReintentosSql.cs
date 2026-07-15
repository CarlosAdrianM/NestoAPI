using Polly;
using System;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// NestoAPI#288 (punto 2): policy común de reintento ante deadlocks de SQL Server (víctima
    /// 1205). Un deadlock es transitorio por definición: SQL Server revierte ENTERA la transacción
    /// víctima y la otra continúa, así que reintentar la operación completa (con contexto/conexión
    /// nuevos) es seguro SIEMPRE que la operación sea idempotente o abra su propia transacción.
    /// Nació como retry artesanal en ContabilidadService (#273, apuntes concurrentes de Cajas/TPV);
    /// al aparecer el segundo caso (la lectura pesada de GestorClientes.ObtenerClientes) se migró
    /// a esta policy Polly compartida.
    /// </summary>
    public static class ReintentosSql
    {
        private const int SQL_DEADLOCK_VICTIM = 1205;

        public static Task<T> ReintentarSiDeadlockAsync<T>(Func<Task<T>> operacion, int maxIntentos = 3, int retrasoBaseMs = 200)
        {
            return Policy
                .Handle<Exception>(EsVictimaDeDeadlock)
                .WaitAndRetryAsync(maxIntentos - 1, intento => TimeSpan.FromMilliseconds(retrasoBaseMs * intento))
                .ExecuteAsync(operacion);
        }

        public static T ReintentarSiDeadlock<T>(Func<T> operacion, int maxIntentos = 3, int retrasoBaseMs = 200)
        {
            return Policy
                .Handle<Exception>(EsVictimaDeDeadlock)
                .WaitAndRetry(maxIntentos - 1, intento => TimeSpan.FromMilliseconds(retrasoBaseMs * intento))
                .Execute(operacion);
        }

        // ¿Hay un SqlException 1205 (elegido como víctima de interbloqueo) en la cadena? Los catch
        // intermedios suelen envolver, así que se recorre la cadena de InnerException completa.
        public static bool EsVictimaDeDeadlock(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (e is SqlException sql && sql.Number == SQL_DEADLOCK_VICTIM)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
