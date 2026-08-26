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

            // NestoAPI#408: el trigger encola una fila POR SENTENCIA, así que un guardado que toca
            // la tabla dos veces deja dos filas para el mismo registro (caso real: asignación de
            // vendedor con dos filas a 13 ms). Publicarlas todas duplica mensajes idénticos, y en
            // Odoo dos mensajes casi simultáneos son dos escrituras (dos correos de asignación):
            // se publica UNA vez por ModificadoId y se marcan sincronizadas TODAS sus filas.
            List<NestoSyncRecord> registrosAgrupados = AgruparPorModificado(registrosParaSincronizar);

            int totalRegistros = registrosAgrupados.Count;

            if (totalRegistros == 0)
            {
                Console.WriteLine($"✅ No hay registros pendientes de sincronización para la tabla {tabla}");
                return true;
            }

            int duplicados = registrosParaSincronizar.Count - totalRegistros;
            Console.WriteLine($"🔄 Procesando {totalRegistros} registros de la tabla {tabla} en lotes de {batchSize}" +
                (duplicados > 0 ? $" ({duplicados} filas duplicadas agrupadas)" : string.Empty));

            // Procesar por lotes
            for (int i = 0; i < totalRegistros; i += batchSize)
            {
                List<NestoSyncRecord> loteRegistros = registrosAgrupados.Skip(i).Take(batchSize).ToList();
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

                            // Marcar sincronizadas TODAS las filas pendientes de este registro
                            // (el representante es la de mayor Id, así que Id <= registro.Id no
                            // toca una fila que se encole DESPUÉS de haber leído: esa se queda
                            // pendiente y la publica la siguiente pasada).
                            _ = await MarcarGrupoSincronizado(tabla, registro);

                            Console.WriteLine($"✅ {tabla} {registro.ModificadoId} sincronizado correctamente (Usuario: {usuario})");
                        }
                        else
                        {
                            Console.WriteLine($"⚠️ No se encontraron entidades para {tabla} {registro.ModificadoId}");

                            // Marcar como sincronizado de todos modos para evitar reprocesamiento
                            _ = await MarcarGrupoSincronizado(tabla, registro);
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

        /// <summary>
        /// NestoAPI#408: de las filas pendientes, UNA por registro modificado — la de mayor Id,
        /// que lleva el usuario del último cambio —, en el orden de llegada. El trigger encola una
        /// fila por sentencia SQL, así que los duplicados son el caso normal, no la excepción.
        /// </summary>
        internal static List<NestoSyncRecord> AgruparPorModificado(List<NestoSyncRecord> registros)
        {
            return registros
                .GroupBy(r => r.ModificadoId?.Trim())
                .Select(g => g.OrderBy(r => r.Id).Last())
                .OrderBy(r => r.Id)
                .ToList();
        }

        /// <summary>
        /// Marca sincronizadas todas las filas pendientes del registro hasta el Id del
        /// representante (incluido). Devuelve cuántas marcó.
        /// </summary>
        private async Task<int> MarcarGrupoSincronizado(string tabla, NestoSyncRecord representante)
        {
            return await _db.Database.ExecuteSqlCommandAsync(
                "UPDATE Nesto_sync SET Sincronizado = @now " +
                "WHERE Tabla = @tabla AND ModificadoId = @modificadoId AND Id <= @id AND Sincronizado IS NULL",
                new SqlParameter("@now", DateTime.Now),
                new SqlParameter("@tabla", tabla),
                new SqlParameter("@modificadoId", representante.ModificadoId),
                new SqlParameter("@id", representante.Id)
            );
        }
    }
}
