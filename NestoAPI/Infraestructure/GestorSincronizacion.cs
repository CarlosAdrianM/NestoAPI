using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Gestor centralizado para la sincronización de entidades con sistemas externos usando la tabla nesto_sync
    /// </summary>
    public class GestorSincronizacion : IGestorSincronizacion
    {
        private readonly NVEntities _db;

        public GestorSincronizacion(NVEntities db)
        {
            _db = db;
        }

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
        public async Task<bool> ProcesarTabla<T>(
            string tabla,
            Func<NestoSyncRecord, Task<List<T>>> obtenerEntidades,
            Func<T, string, Task> publicarEntidad,
            int batchSize = 50,
            int delayMs = 5000
        ) where T : class
        {
            bool todosOK = true;

            // Obtenemos los registros de Nesto_sync que necesitan sincronización
            List<NestoSyncRecord> registrosParaSincronizar = await _db.Database.SqlQuery<NestoSyncRecord>(
                "SELECT Id, Tabla, ModificadoId, Usuario, Sincronizado FROM Nesto_sync WHERE Tabla = @tabla AND Sincronizado IS NULL",
                new SqlParameter("@tabla", tabla)
            ).ToListAsync();

            int totalRegistros = registrosParaSincronizar.Count;

            if (totalRegistros == 0)
            {
                Console.WriteLine($"✅ No hay registros pendientes de sincronización para la tabla {tabla}");
                return true;
            }

            Console.WriteLine($"🔄 Procesando {totalRegistros} registros de la tabla {tabla} en lotes de {batchSize}");

            // Procesar por lotes
            for (int i = 0; i < totalRegistros; i += batchSize)
            {
                List<NestoSyncRecord> loteRegistros = registrosParaSincronizar.Skip(i).Take(batchSize).ToList();
                int loteActual = (i / batchSize) + 1;
                int totalLotes = (int)Math.Ceiling((double)totalRegistros / batchSize);

                Console.WriteLine($"📦 Procesando lote {loteActual}/{totalLotes} ({loteRegistros.Count} registros)");

                foreach (NestoSyncRecord registro in loteRegistros)
                {
                    string usuario = string.IsNullOrWhiteSpace(registro.Usuario) ? "DESCONOCIDO" : registro.Usuario.Trim();

                    try
                    {
                        // Obtener las entidades asociadas a este registro
                        List<T> entidades = await obtenerEntidades(registro);

                        if (entidades != null && entidades.Any())
                        {
                            // Publicar cada entidad con el usuario del registro
                            foreach (T entidad in entidades)
                            {
                                await publicarEntidad(entidad, usuario);
                            }

                            // Actualizar el campo Sincronizado en Nesto_sync
                            await _db.Database.ExecuteSqlCommandAsync(
                                "UPDATE Nesto_sync SET Sincronizado = @now WHERE Id = @id",
                                new SqlParameter("@now", DateTime.Now),
                                new SqlParameter("@id", registro.Id)
                            );

                            Console.WriteLine($"✅ {tabla} {registro.ModificadoId} sincronizado correctamente (Usuario: {usuario})");
                        }
                        else
                        {
                            Console.WriteLine($"⚠️ No se encontraron entidades para {tabla} {registro.ModificadoId}");

                            // Marcar como sincronizado de todos modos para evitar reprocesamiento
                            await _db.Database.ExecuteSqlCommandAsync(
                                "UPDATE Nesto_sync SET Sincronizado = @now WHERE Id = @id",
                                new SqlParameter("@now", DateTime.Now),
                                new SqlParameter("@id", registro.Id)
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        todosOK = false;
                        Console.WriteLine($"❌ Error al sincronizar {tabla} {registro.ModificadoId}: {ex.Message}");
                        // No actualizamos Sincronizado para que se reintente en el próximo ciclo
                    }
                }

                // Esperar antes de procesar el siguiente lote (si no es el último)
                if (i + batchSize < totalRegistros)
                {
                    Console.WriteLine($"⏳ Esperando {delayMs}ms antes del siguiente lote...");
                    await Task.Delay(delayMs);
                }
            }

            string resultado = todosOK ? "✅ ÉXITO" : "⚠️ COMPLETADO CON ERRORES";
            Console.WriteLine($"{resultado}: Sincronización de tabla {tabla} finalizada. Total procesados: {totalRegistros}");

            return todosOK;
        }
    }
}
