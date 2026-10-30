using NestoAPI.Models;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Contadores
{
    /// <summary>
    /// El número de la siguiente nota de entrega (NotasEntrega.NotaEntrega). Único punto de la API que lo saca.
    /// </summary>
    public interface INumeradorNotasEntrega
    {
        /// <summary>Reserva y devuelve el siguiente número. Si el llamante tiene una transacción abierta, va dentro.</summary>
        Task<int> Siguiente(NVEntities db);
    }

    /// <summary>
    /// NestoAPI#756: <c>ContadoresGlobales.NotaEntrega</c> guarda el ÚLTIMO número usado, que es como lo usa Nesto viejo
    /// (suma y usa el valor nuevo). La API leía el contador, lo usaba tal cual y guardaba +1 por EF («siguiente libre»):
    /// cada nota de la API detrás de una de Nesto viejo repetía su número (15 pares sep-oct/26, p. ej. 25791 en 929667
    /// y 929637) y cada nota de Nesto viejo detrás de una de la API dejaba un hueco. UPDATE…OUTPUT sube y lee en una
    /// sola operación, con la misma semántica que Nesto viejo y sin carrera entre dos altas a la vez
    /// (mismo patrón que <see cref="NumeradorTraspasosSql"/>).
    /// </summary>
    public class NumeradorNotasEntregaSql : INumeradorNotasEntrega
    {
        internal const string SQL_SIGUIENTE =
            "UPDATE ContadoresGlobales SET NotaEntrega = NotaEntrega + 1 OUTPUT inserted.NotaEntrega";

        public async Task<int> Siguiente(NVEntities db)
        {
            var numeros = await db.Database.SqlQuery<int>(SQL_SIGUIENTE).ToListAsync().ConfigureAwait(false);
            if (numeros.Count != 1)
            {
                throw new System.InvalidOperationException("No se encontró el registro de ContadoresGlobales");
            }
            return numeros[0];
        }
    }
}
