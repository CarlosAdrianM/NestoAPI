using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Models
{
    public partial class NVEntities
    {
        /// <summary>
        /// Encola el producto en Nesto_sync para que la pasada de sincronización de los 5
        /// minutos lo publique (mismo mecanismo que el trigger y el job nocturno de stocks).
        /// Nesto_sync no está en el modelo EF, así que se inserta por SQL; el método es
        /// VIRTUAL para poder fakearlo en los tests de los controllers que encolan.
        /// No encola si ya hay una fila pendiente del mismo producto (la publicación siempre
        /// manda el estado ACTUAL, con una basta).
        /// </summary>
        public virtual Task<int> EncolarProductoSync(string producto, string usuario)
        {
            return Database.ExecuteSqlCommandAsync(@"
                INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
                SELECT 'Productos', @p0, @p1, GETDATE()
                WHERE NOT EXISTS (SELECT 1 FROM Nesto_sync ns
                                  WHERE ns.Tabla = 'Productos'
                                    AND ns.ModificadoId = @p0
                                    AND ns.Sincronizado IS NULL)",
                new SqlParameter("@p0", producto),
                new SqlParameter("@p1", usuario ?? "NestoAPI"));
        }

        /// <summary>
        /// NestoAPI#433: encola una LISTA de productos en una sola sentencia por lote, en vez de un
        /// viaje a SQL por producto. Marcar la familia Lisap eran 843 idas y vueltas dentro de la
        /// petición HTTP: si el timeout cortaba a mitad, la familia quedaba marcada con la mitad de
        /// sus productos sin encolar y nada lo reintentaba.
        ///
        /// <para>Misma guarda que la de un producto: no reencola lo que ya está pendiente. El
        /// DISTINCT es necesario además para los duplicados DENTRO de la lista, que el NOT EXISTS
        /// no ve (las filas de un mismo INSERT no existen aún unas para otras).</para>
        ///
        /// <para>Va con constructor VALUES y no con STRING_SPLIT porque la BD está en nivel de
        /// compatibilidad 100 (comprobado 01/09/26). Lotes de 500: el límite de SQL Server son
        /// 2.100 parámetros por sentencia.</para>
        /// </summary>
        public virtual async Task<int> EncolarProductosSync(IEnumerable<string> productos, string usuario)
        {
            List<string> limpios = (productos ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct()
                .ToList();

            int encolados = 0;
            const int tamanoLote = 500;
            for (int inicio = 0; inicio < limpios.Count; inicio += tamanoLote)
            {
                List<string> lote = limpios.Skip(inicio).Take(tamanoLote).ToList();
                List<object> parametros = new List<object> { new SqlParameter("@usuario", usuario ?? "NestoAPI") };
                List<string> filas = new List<string>();
                for (int i = 0; i < lote.Count; i++)
                {
                    filas.Add($"(@p{i})");
                    parametros.Add(new SqlParameter($"@p{i}", lote[i]));
                }

                encolados += await Database.ExecuteSqlCommandAsync($@"
                    INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
                    SELECT 'Productos', v.valor, @usuario, GETDATE()
                    FROM (VALUES {string.Join(",", filas)}) v(valor)
                    WHERE NOT EXISTS (SELECT 1 FROM Nesto_sync ns
                                      WHERE ns.Tabla = 'Productos'
                                        AND ns.ModificadoId = v.valor
                                        AND ns.Sincronizado IS NULL)",
                    parametros.ToArray()).ConfigureAwait(false);
            }
            return encolados;
        }
    }
}
