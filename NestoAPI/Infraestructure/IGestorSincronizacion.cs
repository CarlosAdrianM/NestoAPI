using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Interfaz para el gestor de sincronización genérica de entidades con sistemas externos
    /// </summary>
    public interface IGestorSincronizacion
    {
        /// <summary>
        /// Procesa todos los registros pendientes de sincronización para una tabla específica
        /// </summary>
        /// <typeparam name="T">Tipo de entidad a sincronizar</typeparam>
        /// <param name="tabla">Nombre de la tabla en nesto_sync</param>
        /// <param name="obtenerEntidades">Función que obtiene las entidades completas dado un registro de nesto_sync</param>
        /// <param name="publicarEntidad">Función que publica una entidad al sistema externo, recibe el usuario del registro</param>
        /// <param name="batchSize">Tamaño del lote (por defecto 50)</param>
        /// <param name="delayMs">Pausa entre lotes en milisegundos (por defecto 5000)</param>
        /// <returns>True si todos los registros se procesaron correctamente, False si hubo algún error</returns>
        Task<bool> ProcesarTabla<T>(
            string tabla,
            Func<NestoSyncRecord, Task<List<T>>> obtenerEntidades,
            Func<T, string, Task> publicarEntidad,
            int batchSize = 50,
            int delayMs = 5000
        ) where T : class;
    }
}
