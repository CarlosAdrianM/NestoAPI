using NestoAPI.Models;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Contadores
{
    /// <summary>
    /// El número del siguiente traspaso de almacén (PreExtrProducto.NºTraspaso). Único punto de la API que lo saca.
    /// </summary>
    public interface INumeradorTraspasos
    {
        /// <summary>Reserva y devuelve el siguiente número. Si el llamante tiene una transacción abierta, va dentro.</summary>
        Task<int> Siguiente(NVEntities db);
    }

    /// <summary>
    /// <c>ContadoresGlobales.TraspasoAlmacén</c> guarda el ÚLTIMO número usado (como lo usan prdCrearAlbaránVta y
    /// prdContabilizarInventario: suben el contador y usan el valor nuevo). Antes las notas de entrega usaban el valor
    /// tal cual y repetían el número del traspaso anterior (05/10/26: la nota del 29/09 con el 80829 de una reposición).
    /// UPDATE…OUTPUT sube y lee en una sola operación: dos altas a la vez no se llevan el mismo número
    /// (mismo patrón que PedidosCompraService y CrearRemesaService).
    /// </summary>
    public class NumeradorTraspasosSql : INumeradorTraspasos
    {
        internal const string SQL_SIGUIENTE =
            "UPDATE ContadoresGlobales SET TraspasoAlmacén = TraspasoAlmacén + 1 OUTPUT inserted.TraspasoAlmacén";

        public async Task<int> Siguiente(NVEntities db)
        {
            return (await db.Database.SqlQuery<int>(SQL_SIGUIENTE).ToListAsync().ConfigureAwait(false)).Single();
        }
    }
}
