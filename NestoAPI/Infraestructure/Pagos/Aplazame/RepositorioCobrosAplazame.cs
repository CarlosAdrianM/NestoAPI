using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos.Aplazame
{
    /// <summary>Una línea del pedido, lo justo para saber si está servida y cuánto vale.</summary>
    public class LineaPedidoServida
    {
        public short Estado { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>
    /// NestoAPI#663: lo que el servicio de Aplázame lee y escribe de PagosTPV y del pedido. Detrás de una interfaz para
    /// probar la confirmación, la captura y la cancelación sin base de datos.
    /// </summary>
    public interface IRepositorioCobrosAplazame
    {
        Task<PagoTPV> Leer(int idPago);

        /// <summary>El cobro de Aplázame con ese mid (NumeroOrden), o null.</summary>
        Task<PagoTPV> LeerPorMid(string mid);

        /// <summary>Da de alta el cobro y devuelve su Id.</summary>
        Task<int> Crear(PagoTPV pago);

        Task GuardarReferencias(int idPago, string mid, string checkoutId);

        /// <summary>
        /// Cambia el estado SOLO si está en uno de <paramref name="desde"/> (UPDATE condicional: la notificación de Aplázame
        /// y la creación del pedido pueden llegar a la vez). Devuelve si lo ha cambiado.
        /// </summary>
        Task<bool> CambiarEstado(int idPago, IEnumerable<string> desde, string a, DateTime? fechaConfirmacion = null);

        Task GuardarCaptura(int idPago, DateTime fecha, decimal importe);

        Task MarcarAvisoCaptura(int idPago, DateTime fecha);

        /// <summary>Cobros de Aplázame confirmados, sin capturar y con pedido.</summary>
        Task<List<PagoTPV>> LeerConfirmadosSinCapturar();

        Task<List<LineaPedidoServida>> LeerLineasPedido(string empresa, int pedido);
    }

    public class RepositorioCobrosAplazame : IRepositorioCobrosAplazame
    {
        private static NVEntities Contexto()
        {
            NVEntities db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            return db;
        }

        public async Task<PagoTPV> Leer(int idPago)
        {
            using (NVEntities db = Contexto())
            {
                return await db.PagosTPV.AsNoTracking().FirstOrDefaultAsync(p => p.Id == idPago).ConfigureAwait(false);
            }
        }

        public async Task<PagoTPV> LeerPorMid(string mid)
        {
            string tipo = Constantes.TiposPagoTPV.CARRITO_APLAZAME;
            using (NVEntities db = Contexto())
            {
                return await db.PagosTPV.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.NumeroOrden == mid && p.Tipo == tipo)
                    .ConfigureAwait(false);
            }
        }

        public async Task<int> Crear(PagoTPV pago)
        {
            using (NVEntities db = Contexto())
            {
                _ = db.PagosTPV.Add(pago);
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
                return pago.Id;
            }
        }

        public async Task GuardarReferencias(int idPago, string mid, string checkoutId)
        {
            using (NVEntities db = Contexto())
            {
                PagoTPV pago = await db.PagosTPV.FirstAsync(p => p.Id == idPago).ConfigureAwait(false);
                if (mid != null)
                {
                    pago.NumeroOrden = mid;
                }
                if (checkoutId != null)
                {
                    pago.ReferenciaExterna = checkoutId;
                }
                pago.FechaActualizacion = DateTime.Now;
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task<bool> CambiarEstado(int idPago, IEnumerable<string> desde, string a, DateTime? fechaConfirmacion = null)
        {
            List<string> estados = desde.ToList();
            if (!estados.Any())
            {
                return false;
            }
            // Parámetros: @p0 = Id, @p1 = estado nuevo, @p2 = fecha de confirmación (o null), @p3... = estados de partida
            List<object> parametros = new List<object> { idPago, a, (object)fechaConfirmacion ?? DBNull.Value };
            parametros.AddRange(estados);
            string enLista = string.Join(", ", estados.Select((_, i) => "@p" + (i + 3)));
            string sql = "UPDATE dbo.PagosTPV SET Estado = @p1, FechaActualizacion = GETDATE(), " +
                "FechaConfirmacion = COALESCE(FechaConfirmacion, @p2) " +
                $"WHERE Id = @p0 AND Estado IN ({enLista})";
            using (NVEntities db = Contexto())
            {
                int filas = await db.Database.ExecuteSqlCommandAsync(sql, parametros.ToArray()).ConfigureAwait(false);
                return filas > 0;
            }
        }

        public async Task GuardarCaptura(int idPago, DateTime fecha, decimal importe)
        {
            using (NVEntities db = Contexto())
            {
                PagoTPV pago = await db.PagosTPV.FirstAsync(p => p.Id == idPago).ConfigureAwait(false);
                pago.FechaCaptura = fecha;
                pago.ImporteCapturado = importe;
                pago.FechaActualizacion = DateTime.Now;
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task MarcarAvisoCaptura(int idPago, DateTime fecha)
        {
            using (NVEntities db = Contexto())
            {
                PagoTPV pago = await db.PagosTPV.FirstAsync(p => p.Id == idPago).ConfigureAwait(false);
                pago.FechaAvisoCaptura = fecha;
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task<List<PagoTPV>> LeerConfirmadosSinCapturar()
        {
            string tipo = Constantes.TiposPagoTPV.CARRITO_APLAZAME;
            string autorizado = Constantes.EstadosPagoTPV.AUTORIZADO;
            using (NVEntities db = Contexto())
            {
                List<PagoTPV> cobros = await db.PagosTPV.AsNoTracking()
                    .Where(p => p.Tipo == tipo && p.Estado == autorizado && p.FechaCaptura == null && p.Documento != null)
                    .OrderBy(p => p.Id)
                    .ToListAsync()
                    .ConfigureAwait(false);
                // Con pedido de verdad: «RESERVADO» (el pedido se estaba creando) no es un número
                return cobros.Where(p => int.TryParse(p.Documento?.Trim(), out int numero) && numero > 0).ToList();
            }
        }

        public async Task<List<LineaPedidoServida>> LeerLineasPedido(string empresa, int pedido)
        {
            using (NVEntities db = Contexto())
            {
                return await db.LinPedidoVtas.AsNoTracking()
                    .Where(l => l.Empresa == empresa && l.Número == pedido)
                    .Select(l => new LineaPedidoServida { Estado = l.Estado, Total = l.Total })
                    .ToListAsync()
                    .ConfigureAwait(false);
            }
        }
    }
}
