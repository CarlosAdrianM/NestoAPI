using NestoAPI.Models;
using NestoAPI.Models.Rectificativas;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rectificativas
{
    /// <summary>
    /// Issue #87: acceso a la tabla RectificativaPendiente por SQL crudo (no está en el EDMX;
    /// mismo patrón que Cargos/EstadosCCC/Remesas). Pocas filas por pedido: los inserts/deletes
    /// van fila a fila sin problema.
    /// </summary>
    public class AlmacenRectificativasPendientes : IAlmacenRectificativasPendientes
    {
        private readonly NVEntities _db;

        public AlmacenRectificativasPendientes(NVEntities db)
        {
            _db = db;
        }

        public async Task GuardarPendientes(string empresa, int numeroPedido, List<RectificativaPendienteDTO> pendientes)
        {
            foreach (RectificativaPendienteDTO pendiente in pendientes)
            {
                _ = await _db.Database.ExecuteSqlCommandAsync(
                    "INSERT INTO RectificativaPendiente (Empresa, NumeroPedido, NumeroLinea, FacturaOriginalNumero, FacturaOriginalLinea, CantidadRectificada) " +
                    "VALUES (@p0, @p1, @p2, @p3, @p4, @p5)",
                    empresa, numeroPedido, pendiente.NumeroLinea,
                    pendiente.FacturaOriginalNumero, pendiente.FacturaOriginalLinea, pendiente.CantidadRectificada);
            }
        }

        public async Task<List<RectificativaPendienteDTO>> LeerPendientes(string empresa, int numeroPedido)
        {
            return await _db.Database.SqlQuery<RectificativaPendienteDTO>(
                "SELECT NumeroLinea, FacturaOriginalNumero, FacturaOriginalLinea, CantidadRectificada " +
                "FROM RectificativaPendiente WHERE Empresa = @p0 AND NumeroPedido = @p1",
                empresa, numeroPedido).ToListAsync();
        }

        public async Task<List<RectificativaPendienteDTO>> LeerPorFacturasOriginales(string empresa, IList<string> facturas)
        {
            List<string> numeros = (facturas ?? new List<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).Distinct().ToList();
            if (numeros.Count == 0)
            {
                return new List<RectificativaPendienteDTO>();
            }
            var parametros = new List<object> { empresa?.Trim() };
            parametros.AddRange(numeros);
            string enLista = string.Join(", ", numeros.Select((f, i) => $"@p{i + 1}"));
            // La fila sin su línea en el pedido (pedido borrado o línea quitada) ya no abona nada
            return await _db.Database.SqlQuery<RectificativaPendienteDTO>(
                "SELECT rp.NumeroPedido, rp.NumeroLinea, rp.FacturaOriginalNumero, rp.FacturaOriginalLinea, rp.CantidadRectificada " +
                "FROM RectificativaPendiente rp WITH (NOLOCK) " +
                "INNER JOIN LinPedidoVta l WITH (NOLOCK) ON l.Empresa = rp.Empresa AND l.Número = rp.NumeroPedido AND l.[Nº Orden] = rp.NumeroLinea " +
                $"WHERE rp.Empresa = @p0 AND rp.FacturaOriginalNumero IN ({enLista})",
                parametros.ToArray()).ToListAsync();
        }

        public async Task BorrarPendientes(string empresa, int numeroPedido, List<int> numerosLinea)
        {
            foreach (int numeroLinea in numerosLinea)
            {
                _ = await _db.Database.ExecuteSqlCommandAsync(
                    "DELETE FROM RectificativaPendiente WHERE Empresa = @p0 AND NumeroPedido = @p1 AND NumeroLinea = @p2",
                    empresa, numeroPedido, numeroLinea);
            }
        }
    }
}
