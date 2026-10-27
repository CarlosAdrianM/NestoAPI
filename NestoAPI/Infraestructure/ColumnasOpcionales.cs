using NestoAPI.Models;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// NestoAPI#716: columnas y tablas que añade un script sa que puede no haberse lanzado todavía. Se comprueban con
    /// COL_LENGTH / OBJECT_ID UNA sola vez por proceso y se recuerda el resultado: sin el script, todo sigue igual
    /// (no se escribe nada y no se loguea nada); tras lanzarlo, basta reciclar el app pool (o el siguiente despliegue).
    /// <para>Las columnas no están en el EDMX a propósito (regla «EDMX a mano → validar metadatos»): son opcionales y
    /// se leen y escriben con SQL plano.</para>
    /// </summary>
    public static class ColumnasOpcionales
    {
        private static readonly ConcurrentDictionary<string, bool> cache = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Para los tests: fuerza el resultado de todas las comprobaciones (null = volver a preguntar a la BD).</summary>
        internal static bool? ForzarParaTests { get; set; }

        /// <summary>True si TODAS las columnas existen en la tabla (dbo). Nunca lanza: ante error, false y sin cache.</summary>
        public static bool Existen(NVEntities db, string tabla, params string[] columnas)
        {
            if (ForzarParaTests.HasValue)
            {
                return ForzarParaTests.Value;
            }
            if (db == null || string.IsNullOrWhiteSpace(tabla) || columnas == null || columnas.Length == 0)
            {
                return false;
            }
            string clave = tabla + ":" + string.Join(",", columnas);
            if (cache.TryGetValue(clave, out bool conocido))
            {
                return conocido;
            }
            try
            {
                string condiciones = string.Join(" AND ", columnas.Select((c, i) => $"COL_LENGTH(@p0, @p{i + 1}) IS NOT NULL"));
                object[] parametros = new object[] { "dbo." + tabla }.Concat(columnas.Cast<object>()).ToArray();
                bool existen = db.Database.SqlQuery<int>($"SELECT CASE WHEN {condiciones} THEN 1 ELSE 0 END", parametros).FirstOrDefault() == 1;
                // Solo se recuerda el «sí»: si el script se lanza con la API arrancada, la siguiente llamada ya lo ve.
                if (existen)
                {
                    cache[clave] = true;
                }
                return existen;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>True si la tabla (dbo) existe. Misma política que <see cref="Existen"/>.</summary>
        public static bool ExisteTabla(NVEntities db, string tabla)
        {
            if (ForzarParaTests.HasValue)
            {
                return ForzarParaTests.Value;
            }
            if (db == null || string.IsNullOrWhiteSpace(tabla))
            {
                return false;
            }
            string clave = "TABLA:" + tabla;
            if (cache.TryGetValue(clave, out bool conocido))
            {
                return conocido;
            }
            try
            {
                bool existe = db.Database.SqlQuery<int>("SELECT CASE WHEN OBJECT_ID(@p0) IS NOT NULL THEN 1 ELSE 0 END", "dbo." + tabla).FirstOrDefault() == 1;
                if (existe)
                {
                    cache[clave] = true;
                }
                return existe;
            }
            catch
            {
                return false;
            }
        }

        internal static void VaciarCache()
        {
            cache.Clear();
        }
    }
}
