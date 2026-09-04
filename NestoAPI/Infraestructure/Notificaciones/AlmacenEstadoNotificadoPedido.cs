using NestoAPI.Models;
using NestoAPI.Models.Notificaciones;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Notificaciones
{
    /// <summary>
    /// TNV#66: lo que sabe el sistema de avisos sobre cada pedido. Ver
    /// <see cref="AlmacenEstadoNotificadoPedido"/>.
    /// </summary>
    public interface IAlmacenEstadoNotificadoPedido
    {
        /// <summary>Lo guardado de esos pedidos, indexado por número. Los que no estén, es que nunca se han visto.</summary>
        Dictionary<int, EstadoNotificadoPedido> Obtener(string empresa, IReadOnlyCollection<int> pedidos);

        /// <summary>Deja constancia del estado visto ahora. Solo pisa la fecha si el estado ha cambiado.</summary>
        void RegistrarEstado(string empresa, int pedido, string estado, DateTime ahora);

        /// <summary>Deja constancia de que ya se le ha avisado de ese estado.</summary>
        void RegistrarAviso(string empresa, int pedido, string estado, DateTime ahora);
    }

    /// <summary>
    /// TNV#66: implementación con SQL crudo sobre NVEntities (la tabla NO está en el EDMX), mismo
    /// patrón que AlmacenFacturasAmazon (#366).
    /// </summary>
    public class AlmacenEstadoNotificadoPedido : IAlmacenEstadoNotificadoPedido
    {
        private const string COLUMNAS = "Empresa, Pedido, Estado, FechaEstado, EstadoNotificado, FechaNotificacion";

        private readonly NVEntities _db;

        public AlmacenEstadoNotificadoPedido(NVEntities db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public Dictionary<int, EstadoNotificadoPedido> Obtener(string empresa, IReadOnlyCollection<int> pedidos)
        {
            if (pedidos == null || pedidos.Count == 0)
            {
                return new Dictionary<int, EstadoNotificadoPedido>();
            }

            // Los números de pedido son int (no hay inyección posible) y la tanda de una pasada es
            // corta, así que la lista va inline en el IN.
            string listaPedidos = string.Join(",", pedidos.Distinct());

            return _db.Database.SqlQuery<EstadoNotificadoPedido>(
                    $"SELECT {COLUMNAS} FROM dbo.NotificacionesEstadoPedido " +
                    $"WHERE Empresa=@p0 AND Pedido IN ({listaPedidos})",
                    empresa?.Trim())
                .ToList()
                .ToDictionary(f => f.Pedido);
        }

        public void RegistrarEstado(string empresa, int pedido, string estado, DateTime ahora)
        {
            // La fecha solo se pisa cuando el estado CAMBIA: es "desde cuándo está así", y de ella
            // depende el recordatorio de pago. Si se refrescara en cada pasada, un pedido sin
            // pagar nunca cumpliría las horas de espera y no se avisaría jamás.
            int actualizadas = _db.Database.ExecuteSqlCommand(
                "UPDATE dbo.NotificacionesEstadoPedido " +
                "SET FechaEstado = CASE WHEN Estado <> @p2 THEN @p3 ELSE FechaEstado END, Estado = @p2 " +
                "WHERE Empresa = @p0 AND Pedido = @p1",
                empresa?.Trim(), pedido, estado, ahora);

            if (actualizadas == 0)
            {
                _ = _db.Database.ExecuteSqlCommand(
                    "INSERT INTO dbo.NotificacionesEstadoPedido (Empresa, Pedido, Estado, FechaEstado) " +
                    "VALUES (@p0, @p1, @p2, @p3)",
                    empresa?.Trim(), pedido, estado, ahora);
            }
        }

        public void RegistrarAviso(string empresa, int pedido, string estado, DateTime ahora)
        {
            _ = _db.Database.ExecuteSqlCommand(
                "UPDATE dbo.NotificacionesEstadoPedido " +
                "SET EstadoNotificado = @p2, FechaNotificacion = @p3 " +
                "WHERE Empresa = @p0 AND Pedido = @p1",
                empresa?.Trim(), pedido, estado, ahora);
        }
    }
}
