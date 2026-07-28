using System;
using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;
using NestoAPI.Models.CanalesExternos;

namespace NestoAPI.Infraestructure.CanalesExternos.Amazon
{
    /// <summary>
    /// NestoAPI#366: implementación con SQL crudo sobre NVEntities (la tabla NO está en el EDMX),
    /// mismo patrón que AmazonCredencialStore (#225).
    /// </summary>
    public class AlmacenFacturasAmazon : IAlmacenFacturasAmazon
    {
        private const string Columnas =
            "Id, Empresa, Pedido, NumeroFactura, AmazonOrderId, MarketplaceId, FeedId, Estado, Resultado, FechaEnvio, FechaResultado, Usuario";

        private readonly NVEntities _db;

        public AlmacenFacturasAmazon(NVEntities db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public AmazonFacturaSubida Obtener(string empresa, int pedido)
        {
            return _db.Database.SqlQuery<AmazonFacturaSubida>(
                $"SELECT {Columnas} FROM dbo.AmazonFacturasSubidas WHERE Empresa=@p0 AND Pedido=@p1",
                empresa?.Trim(), pedido).FirstOrDefault();
        }

        public IReadOnlyList<AmazonFacturaSubida> ObtenerVarias(string empresa, IReadOnlyCollection<int> pedidos)
        {
            if (pedidos == null || pedidos.Count == 0)
            {
                return new List<AmazonFacturaSubida>();
            }
            // Los números de pedido son int (no hay inyección posible) y las listas del grid son
            // cortas, así que la lista va inline en el IN.
            string listaPedidos = string.Join(",", pedidos.Distinct());
            return _db.Database.SqlQuery<AmazonFacturaSubida>(
                $"SELECT {Columnas} FROM dbo.AmazonFacturasSubidas WHERE Empresa=@p0 AND Pedido IN ({listaPedidos})",
                empresa?.Trim()).ToList();
        }

        public void Registrar(AmazonFacturaSubida fila)
        {
            int actualizadas = _db.Database.ExecuteSqlCommand(
                "UPDATE dbo.AmazonFacturasSubidas SET NumeroFactura=@p2, AmazonOrderId=@p3, MarketplaceId=@p4, " +
                "FeedId=@p5, Estado=@p6, Resultado=NULL, FechaEnvio=GETDATE(), FechaResultado=NULL, Usuario=@p7 " +
                "WHERE Empresa=@p0 AND Pedido=@p1",
                fila.Empresa?.Trim(), fila.Pedido, fila.NumeroFactura, fila.AmazonOrderId,
                fila.MarketplaceId, fila.FeedId, fila.Estado, (object)fila.Usuario ?? DBNull.Value);
            if (actualizadas == 0)
            {
                _ = _db.Database.ExecuteSqlCommand(
                    "INSERT INTO dbo.AmazonFacturasSubidas (Empresa, Pedido, NumeroFactura, AmazonOrderId, MarketplaceId, FeedId, Estado, FechaEnvio, Usuario) " +
                    "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, GETDATE(), @p7)",
                    fila.Empresa?.Trim(), fila.Pedido, fila.NumeroFactura, fila.AmazonOrderId,
                    fila.MarketplaceId, fila.FeedId, fila.Estado, (object)fila.Usuario ?? DBNull.Value);
            }
        }

        public IReadOnlyList<AmazonFacturaSubida> ObtenerPendientesResultado()
        {
            return _db.Database.SqlQuery<AmazonFacturaSubida>(
                $"SELECT {Columnas} FROM dbo.AmazonFacturasSubidas WHERE Estado=@p0",
                EstadosFacturaAmazon.ENVIADA).ToList();
        }

        public void ActualizarResultado(int id, string estado, string resultado)
        {
            _ = _db.Database.ExecuteSqlCommand(
                "UPDATE dbo.AmazonFacturasSubidas SET Estado=@p1, Resultado=@p2, FechaResultado=GETDATE() WHERE Id=@p0",
                id, estado, (object)resultado ?? DBNull.Value);
        }
    }
}
