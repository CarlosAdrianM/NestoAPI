using NestoAPI.Models;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Un producto de un pedido que el mozo dio por «No está» en Ariadna y que sigue en el pedido.</summary>
    public class FaltaSinQuitar
    {
        public int Picking { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>
    /// Nesto#508: lo que falta en el picking de un pedido y todavía no se ha quitado de él. Al terminar la salida en
    /// Ariadna lo que falta sale del pedido (NestoAPI#556); hasta entonces la falta solo está apuntada en
    /// PreparacionEscaneos y un albarán se llevaría un producto que no ha salido.
    /// </summary>
    public class FaltasSinQuitarSalida
    {
        // Solo las líneas de producto que siguen en un picking (estado 1). Si el pedido tiene dos líneas del mismo
        // producto en el mismo picking, la falta se cuenta una vez (DISTINCT). Las faltas son del picking: si otro
        // pedido del mismo picking lleva el mismo producto, el aviso sale en los dos, como pide la issue.
        internal const string SQL_FALTAS_SIN_QUITAR = @"
WITH lineas AS (
    SELECT DISTINCT l.Picking, l.Producto
    FROM LinPedidoVta l
    WHERE l.Empresa = @p0 AND l.[Número] = @p1 AND l.Estado = 1 AND l.TipoLinea = 1 AND l.Picking > 0
)
SELECT li.Picking, RTRIM(li.Producto) AS Producto, CAST(SUM(e.Cantidad) AS int) AS Cantidad
FROM lineas li
JOIN PreparacionEscaneos e ON e.Empresa = @p0 AND e.TipoOrigen = 'PICK' AND e.NumeroOrigen = li.Picking
    AND e.Producto = li.Producto AND e.Fase = 'PICK' AND e.Metodo = 'FALTA'
{0}
GROUP BY li.Picking, li.Producto
HAVING SUM(e.Cantidad) > 0
ORDER BY li.Picking, li.Producto";

        // Una salida terminada ya quitó lo que faltaba (Ariadna#6 la apunta): sus faltas no cuentan. Si la tabla aún
        // no existe (script sin lanzar), no se ha terminado ninguna salida en firme y no hace falta el filtro.
        internal const string FILTRO_TERMINADAS = @"WHERE NOT EXISTS (SELECT 1 FROM dbo.PreparacionSalidasTerminadas t
    WHERE t.Empresa = @p0 AND t.TipoOrigen = 'PICK' AND t.NumeroOrigen = li.Picking)";

        internal const string SQL_HAY_TERMINADAS = @"
SELECT CAST(CASE WHEN OBJECT_ID('dbo.PreparacionSalidasTerminadas') IS NULL THEN 0 ELSE 1 END AS bit)";

        private readonly NVEntities db;

        public FaltasSinQuitarSalida(NVEntities db)
        {
            this.db = db;
        }

        public async Task<List<FaltaSinQuitar>> Leer(string empresa, int pedido)
        {
            bool hayTerminadas = await db.Database.SqlQuery<bool>(SQL_HAY_TERMINADAS).FirstAsync().ConfigureAwait(false);
            string sql = string.Format(SQL_FALTAS_SIN_QUITAR, hayTerminadas ? FILTRO_TERMINADAS : string.Empty);
            return await db.Database.SqlQuery<FaltaSinQuitar>(sql, empresa, pedido).ToListAsync().ConfigureAwait(false);
        }

        public static string Mensaje(int pedido, IEnumerable<FaltaSinQuitar> faltas)
        {
            List<FaltaSinQuitar> lista = faltas.ToList();
            string pickings = string.Join(", ", lista.Select(f => f.Picking).Distinct());
            string productos = string.Join(", ", lista.Select(f => $"{f.Cantidad} {(f.Cantidad == 1 ? "ud." : "uds.")} de {f.Producto?.Trim()}"));
            return $"El pedido {pedido} tiene faltas sin quitar en el picking {pickings} (Ariadna): {productos}. " +
                "Que el mozo termine la salida en Ariadna (quita lo que falta) o deshaga el «No está» antes de hacer el albarán.";
        }
    }
}
