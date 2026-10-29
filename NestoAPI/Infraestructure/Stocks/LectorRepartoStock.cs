using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace NestoAPI.Infraestructure.Stocks
{
    /// <summary>
    /// NestoAPI#730: los datos con los que se reparte el stock por antigüedad (<see cref="GestorReservasStock.Reservar"/>). Es
    /// la MISMA lectura para el picking (<see cref="RellenadorPickingService"/>, <see cref="RellenadorStocksService"/>) y para
    /// el reparto de un solo pedido (<see cref="RepartoStockPorAntiguedad"/>, fecha de entrega a la agencia y avisos de
    /// pedidos en riesgo), para que los dos digan lo mismo (Carlos, 29/10/26: «extraerlo y centralizarlo, no copiarlo»).
    /// </summary>
    public interface ILectorRepartoStock
    {
        /// <summary>
        /// Una fila por producto pedido: stock bruto de Algete (<see cref="StockProducto.StockDisponible"/>), de la tienda y
        /// Alcobendas (<see cref="StockProducto.StockTienda"/>) y lo que viene de camino por reposición
        /// (<see cref="StockProducto.EnCamino"/>).
        /// </summary>
        List<StockProducto> LeerStocks(IEnumerable<string> productos);

        /// <summary>
        /// TODAS las líneas que compiten por el stock de esos productos: pendientes y en curso (-1..1) de las empresas por
        /// defecto y espejo, de cualquier almacén, tengan o no picking y sin mirar la fecha de entrega. Cantidad = Cantidad −
        /// Recoger. Ordenadas como las reparte el picking: por Fecha_Modificación y luego por Nº_Orden.
        /// </summary>
        List<LineaPedidoPicking> LeerLineasPendientes(IEnumerable<string> productos);
    }

    /// <summary>NestoAPI#730: <see cref="ILectorRepartoStock"/> con EF. No se testa porque lee de la base de datos.</summary>
    public class LectorRepartoStock : ILectorRepartoStock
    {
        private readonly NVEntities db;

        public LectorRepartoStock(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// Para leer desde fuera del picking (cálculo de la fecha de entrega, avisos de riesgo), que puede ir dentro del
        /// TransactionScope de quien graba el pedido (UnirPedidos): cada lectura con su propio contexto, fuera de la
        /// transacción y sin bloqueos (READ UNCOMMITTED, como el NOLOCK de <see cref="PedidosVenta.RepositorioSombraModoServicioSql"/>),
        /// para no quedarse esperando a las filas que tiene bloqueadas esa misma transacción.
        /// </summary>
        public static ILectorRepartoStock SinBloqueos() => new LectorRepartoStockSinBloqueos();

        public List<StockProducto> LeerStocks(IEnumerable<string> productos)
        {
            List<string> pedidos = (productos ?? Enumerable.Empty<string>()).ToList();
            List<string> lista = pedidos.Where(p => p != null).Distinct().ToList();
            if (!lista.Any())
            {
                return pedidos.Select(p => new StockProducto { Producto = p }).ToList();
            }
            // Tres consultas agrupadas en vez de tres por producto (la plantilla calcula la fecha de entrega a menudo). Mismas
            // sumas que antes: (int)Cantidad fila a fila.
            Dictionary<string, int> algete = Agrupar(db.ExtractosProducto
                .Where(e => lista.Contains(e.Número) && e.Almacén == Constantes.Productos.ALMACEN_POR_DEFECTO)
                .GroupBy(e => e.Número)
                .Select(g => new { Producto = g.Key, Cantidad = g.Sum(e => (int)e.Cantidad) })
                .ToList()
                .Select(f => new KeyValuePair<string, int>(f.Producto, f.Cantidad)));
            Dictionary<string, int> tienda = Agrupar(db.ExtractosProducto
                .Where(e => lista.Contains(e.Número) && (e.Almacén == Constantes.Productos.ALMACEN_TIENDA || e.Almacén == Constantes.Almacenes.ALCOBENDAS))
                .GroupBy(e => e.Número)
                .Select(g => new { Producto = g.Key, Cantidad = g.Sum(e => (int)e.Cantidad) })
                .ToList()
                .Select(f => new KeyValuePair<string, int>(f.Producto, f.Cantidad)));
            // NestoAPI#482 (modo 3): reposiciones generadas y sin contabilizar, hacia Algete o hacia
            // una tienda. Mismo criterio que prdRellenarReposicionStock (NºTraspaso <> 0).
            Dictionary<string, int> enCamino = Agrupar(db.PreExtrProductos
                .Where(e => lista.Contains(e.Número) && e.NºTraspaso != null && e.NºTraspaso != 0
                    && (e.Almacén == Constantes.Almacenes.ALGETE || e.Almacén == Constantes.Almacenes.REINA || e.Almacén == Constantes.Almacenes.ALCOBENDAS))
                .GroupBy(e => e.Número)
                .Select(g => new { Producto = g.Key, Cantidad = g.Sum(e => (int)e.Cantidad) })
                .ToList()
                .Select(f => new KeyValuePair<string, int>(f.Producto, f.Cantidad)));

            return pedidos.Select(s => new StockProducto
            {
                Producto = s,
                StockDisponible = Valor(algete, s),
                StockTienda = Valor(tienda, s),
                EnCamino = Valor(enCamino, s)
            }).ToList();
        }

        // SQL Server compara sin espacios finales ni mayúsculas: aquí, igual.
        private static string Clave(string producto) => producto?.Trim().ToUpperInvariant() ?? string.Empty;

        private static Dictionary<string, int> Agrupar(IEnumerable<KeyValuePair<string, int>> filas)
        {
            var resultado = new Dictionary<string, int>();
            foreach (KeyValuePair<string, int> fila in filas)
            {
                string clave = Clave(fila.Key);
                resultado[clave] = (resultado.TryGetValue(clave, out int ya) ? ya : 0) + fila.Value;
            }
            return resultado;
        }

        private static int Valor(Dictionary<string, int> valores, string producto) =>
            valores.TryGetValue(Clave(producto), out int valor) ? valor : 0;

        public List<LineaPedidoPicking> LeerLineasPendientes(IEnumerable<string> productos)
        {
            List<string> lista = (productos ?? Enumerable.Empty<string>()).Where(p => p != null).Distinct().ToList();
            if (!lista.Any())
            {
                return new List<LineaPedidoPicking>();
            }
            // TipoLinea != null: el picking ya ha parado antes si hay alguna (RellenadorPickingService); fuera del picking, una
            // línea mal grabada no puede tumbar el cálculo.
            return db.LinPedidoVtas
                .Where(l => (l.Estado == Constantes.EstadosLineaVenta.PENDIENTE || l.Estado == Constantes.EstadosLineaVenta.EN_CURSO)
                    && (l.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || l.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO)
                    && l.TipoLinea != null
                    && lista.Contains(l.Producto))
                .Select(l => new LineaPedidoPicking
                {
                    Id = l.Nº_Orden,
                    Empresa = l.Empresa,
                    NumeroPedido = l.Número,
                    TipoLinea = (byte)l.TipoLinea,
                    Almacen = l.Almacén,
                    Producto = l.Producto,
                    Cantidad = (int)l.Cantidad - l.Recoger,
                    CantidadRecogida = l.Recoger,
                    BaseImponible = l.Base_Imponible,
                    CantidadReservada = 0,
                    FechaEntrega = l.Fecha_Entrega,
                    EsSobrePedido = l.EstadoProducto != Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO && !l.LineaParcial,
                    FechaModificacion = l.Fecha_Modificación,
                    EsPedidoEspecial = l.TipoLinea == Constantes.TiposLineaVenta.TEXTO && db.PedidosEspeciales.FirstOrDefault(e => e.NºOrdenVta == l.Nº_Orden) != null
                })
                .ToList()
                .OrderBy(l => l.FechaModificacion).ThenBy(l => l.Id)
                .ToList();
        }

        private class LectorRepartoStockSinBloqueos : ILectorRepartoStock
        {
            public List<StockProducto> LeerStocks(IEnumerable<string> productos)
                => SinBloqueos(lector => lector.LeerStocks(productos));

            public List<LineaPedidoPicking> LeerLineasPendientes(IEnumerable<string> productos)
                => SinBloqueos(lector => lector.LeerLineasPendientes(productos));

            private static T SinBloqueos<T>(Func<LectorRepartoStock, T> leer)
            {
                using (new System.Transactions.TransactionScope(System.Transactions.TransactionScopeOption.Suppress))
                using (var contexto = new NVEntities())
                {
                    contexto.Configuration.LazyLoadingEnabled = false;
                    contexto.Configuration.ProxyCreationEnabled = false;
                    System.Data.Common.DbConnection conexion = contexto.Database.Connection;
                    conexion.Open();
                    try
                    {
                        _ = contexto.Database.ExecuteSqlCommand(System.Data.Entity.TransactionalBehavior.DoNotEnsureTransaction,
                            "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED");
                        return leer(new LectorRepartoStock(contexto));
                    }
                    finally
                    {
                        // La conexión vuelve al pool: que no se lleve el nivel de aislamiento a quien la coja después.
                        try
                        {
                            if (conexion.State == ConnectionState.Open)
                            {
                                _ = contexto.Database.ExecuteSqlCommand(System.Data.Entity.TransactionalBehavior.DoNotEnsureTransaction,
                                    "SET TRANSACTION ISOLATION LEVEL READ COMMITTED");
                            }
                        }
                        catch
                        {
                            // Si falla, la conexión se cierra igual; no tapa el error de la lectura.
                        }
                        conexion.Close();
                    }
                }
            }
        }
    }
}
