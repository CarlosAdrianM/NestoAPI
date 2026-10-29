using NestoAPI.Models;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>
    /// NestoAPI#613 (Carlos, 29/10/26): el pedido mínimo para usar el cupón que gana quien paga la señal lo define cada evento
    /// (Eventos.PedidoMinimoCupon, Scripts/Issue613_CuponPedidoMinimoPorEvento.sql; NULL = 0, cualquier importe). Fuera del
    /// EDMX, por SQL y comprobando antes que existe la columna (como #732). Sin el script: todos los eventos 0 y guardar contesta
    /// false.
    /// </summary>
    public interface IRepositorioPedidoMinimoCuponEventos
    {
        /// <summary>El pedido mínimo de cada evento que lo tiene distinto de 0 (los que no salen, 0).</summary>
        Task<Dictionary<int, decimal>> Leer(IReadOnlyCollection<int> eventos);
        /// <summary>False sin la columna.</summary>
        Task<bool> Guardar(int eventoId, decimal pedidoMinimo);
    }

    public class RepositorioPedidoMinimoCuponEventos : IRepositorioPedidoMinimoCuponEventos
    {
        internal const string SQL_EXISTE_COLUMNA = "SELECT CAST(CASE WHEN COL_LENGTH('dbo.Eventos', 'PedidoMinimoCupon') IS NULL THEN 0 ELSE 1 END AS bit)";
        internal const string SQL_LEER = "SELECT Id AS EventoId, CAST(PedidoMinimoCupon AS decimal(10, 2)) AS PedidoMinimo FROM dbo.Eventos WITH (NOLOCK) " +
            "WHERE PedidoMinimoCupon <> 0 AND Id IN ({0})";
        internal const string SQL_GUARDAR = "UPDATE dbo.Eventos SET PedidoMinimoCupon = @minimo WHERE Id = @id";

        private static readonly ComprobadorEsquemaEventos columna = new ComprobadorEsquemaEventos(SQL_EXISTE_COLUMNA);
        private readonly NVEntities db;

        public RepositorioPedidoMinimoCuponEventos(NVEntities db)
        {
            this.db = db;
        }

        public async Task<Dictionary<int, decimal>> Leer(IReadOnlyCollection<int> eventos)
        {
            List<int> ids = (eventos ?? new List<int>()).Distinct().ToList();
            var resultado = new Dictionary<int, decimal>();
            if (!ids.Any() || !columna.Existe(db))
            {
                return resultado;
            }
            foreach (List<int> tanda in RepositorioPersonaAsistenteEventos.Tandas(ids, 500))
            {
                foreach (FilaPedidoMinimo fila in await db.Database.SqlQuery<FilaPedidoMinimo>(string.Format(SQL_LEER, string.Join(",", tanda)))
                    .ToListAsync().ConfigureAwait(false))
                {
                    resultado[fila.EventoId] = fila.PedidoMinimo ?? 0;
                }
            }
            return resultado;
        }

        public async Task<bool> Guardar(int eventoId, decimal pedidoMinimo)
        {
            if (!columna.Existe(db))
            {
                return false;
            }
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_GUARDAR,
                new SqlParameter("@minimo", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = pedidoMinimo },
                new SqlParameter("@id", SqlDbType.Int) { Value = eventoId }).ConfigureAwait(false);
            return true;
        }

        private class FilaPedidoMinimo
        {
            public int EventoId { get; set; }
            public decimal? PedidoMinimo { get; set; }
        }
    }
}
