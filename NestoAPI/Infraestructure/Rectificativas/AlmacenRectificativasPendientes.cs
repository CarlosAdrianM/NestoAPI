using NestoAPI.Models;
using NestoAPI.Models.Rectificativas;
using System.Collections.Generic;
using System.Data.Entity;
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
