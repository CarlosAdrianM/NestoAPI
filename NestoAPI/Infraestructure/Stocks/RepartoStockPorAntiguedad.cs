using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Stocks
{
    /// <summary>NestoAPI#730: una línea del pedido que se quiere repartir.</summary>
    public class LineaRepartoStock
    {
        /// <summary>Nº_Orden de la línea, si ya está grabada (null o 0 si es nueva).</summary>
        public int? Id { get; set; }
        public string Producto { get; set; }
        public string Almacen { get; set; }
        /// <summary>Cantidad − Recoger, como en el picking.</summary>
        public int Cantidad { get; set; }
        /// <summary>Su Fecha_Modificación. Null en una línea nueva (o cambiada en la plantilla): va la última, como cuando se grabe.</summary>
        public DateTime? FechaModificacion { get; set; }
    }

    /// <summary>NestoAPI#730: lo que le toca a cada línea de un pedido si ahora mismo se sacara el picking.</summary>
    public interface IRepartoStockPorAntiguedad
    {
        /// <summary>
        /// Por cada línea de <paramref name="lineas"/> (en el mismo orden), las unidades que el picking le reservaría ahora:
        /// las de Algete en las líneas de Algete (y en las de tienda, lo que no tenga la tienda) y las de la tienda en las de
        /// tienda y Alcobendas. Lo que falta hasta la cantidad es lo que no hay.
        /// </summary>
        /// <param name="pedido">El pedido al que pertenecen (sus líneas grabadas se sustituyen por <paramref name="lineas"/>);
        /// null si aún no está grabado.</param>
        /// <param name="ahora">La Fecha_Modificación que tendrían las líneas nuevas al grabarlas.</param>
        int[] UnidadesReservadas(string empresa, int? pedido, IReadOnlyList<LineaRepartoStock> lineas, DateTime ahora);
    }

    /// <summary>
    /// NestoAPI#730 (sugerencia 861, 29/10/26): el reparto del stock por ANTIGÜEDAD, el del picking, para un solo pedido. Lee con
    /// <see cref="ILectorRepartoStock"/> (la misma lectura que el picking) TODAS las líneas pendientes de los productos del
    /// pedido, pone las del pedido en su sitio y aplica <see cref="GestorReservasStock.Reservar"/> (el mismo código que el
    /// picking): por Fecha_Modificación y Nº_Orden, a cada línea min(pendiente, lo que queda).
    ///
    /// <para>Antes, la fecha de entrega a la agencia y los avisos de pedidos en riesgo restaban al stock TODOS los pendientes
    /// de los demás pedidos, sin mirar quién iba antes: con 4 uds en Algete y pedidos de 1, 2 y 2 (43818: 929498, 929440 y
    /// 929547) marcaban en riesgo los tres, y el picking sirvió los dos primeros.</para>
    /// </summary>
    public class RepartoStockPorAntiguedad : IRepartoStockPorAntiguedad
    {
        private readonly ILectorRepartoStock lector;

        public RepartoStockPorAntiguedad(ILectorRepartoStock lector)
        {
            this.lector = lector ?? throw new ArgumentNullException(nameof(lector));
        }

        /// <summary>El de producción: lee con su propio contexto, fuera de la transacción de quien llama y sin bloqueos.</summary>
        public static IRepartoStockPorAntiguedad PorDefecto() => new RepartoStockPorAntiguedad(LectorRepartoStock.SinBloqueos());

        public int[] UnidadesReservadas(string empresa, int? pedido, IReadOnlyList<LineaRepartoStock> lineas, DateTime ahora)
        {
            lineas = lineas ?? new List<LineaRepartoStock>();
            string empresaPedido = Normalizar(empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            List<string> productos = lineas
                .Where(l => l != null && l.Cantidad != 0 && !string.IsNullOrWhiteSpace(l.Producto))
                .Select(l => Normalizar(l.Producto))
                .Distinct()
                .ToList();
            if (!productos.Any())
            {
                return new int[lineas.Count];
            }

            List<StockProducto> stocks = (lector.LeerStocks(productos) ?? new List<StockProducto>())
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Producto))
                .GroupBy(s => Normalizar(s.Producto))
                .Select(g => new StockProducto
                {
                    Producto = g.Key,
                    StockDisponible = g.Sum(s => s.StockDisponible),
                    StockTienda = g.Sum(s => s.StockTienda),
                    EnCamino = g.Sum(s => s.EnCamino)
                })
                .ToList();
            foreach (string producto in productos.Where(p => !stocks.Any(s => s.Producto == p)))
            {
                stocks.Add(new StockProducto { Producto = producto });
            }

            List<LineaPedidoPicking> grabadas = (lector.LeerLineasPendientes(productos) ?? new List<LineaPedidoPicking>())
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.Producto))
                .ToList();
            foreach (LineaPedidoPicking grabada in grabadas)
            {
                grabada.Producto = Normalizar(grabada.Producto);
                grabada.Almacen = Normalizar(grabada.Almacen);
            }
            bool EsDelPedido(LineaPedidoPicking l) => pedido.HasValue && l.NumeroPedido == pedido.Value
                && string.Equals(Normalizar(l.Empresa), empresaPedido, StringComparison.OrdinalIgnoreCase);

            // Las líneas del pedido, en el lugar que les da su Fecha_Modificación. Una línea grabada que no ha cambiado de
            // cantidad conserva la suya (la de la BD); una nueva o cambiada va al final, como cuando se grabe.
            int siguienteId = int.MaxValue - lineas.Count;
            var idsUsados = new HashSet<int>();
            var propias = new LineaPedidoPicking[lineas.Count];
            for (int i = 0; i < lineas.Count; i++)
            {
                LineaRepartoStock linea = lineas[i];
                siguienteId++;
                if (linea == null || linea.Cantidad == 0 || string.IsNullOrWhiteSpace(linea.Producto))
                {
                    continue;
                }
                int? id = linea.Id > 0 && idsUsados.Add(linea.Id.Value) ? linea.Id : null;
                DateTime? fecha = linea.FechaModificacion;
                if (!fecha.HasValue && id.HasValue)
                {
                    LineaPedidoPicking enBd = grabadas.FirstOrDefault(g => g.Id == id.Value);
                    if (enBd != null && enBd.Cantidad == linea.Cantidad)
                    {
                        fecha = enBd.FechaModificacion;
                    }
                }
                propias[i] = new LineaPedidoPicking
                {
                    Id = id ?? siguienteId,
                    Empresa = empresaPedido,
                    NumeroPedido = pedido ?? 0,
                    TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                    Producto = Normalizar(linea.Producto),
                    Almacen = Normalizar(linea.Almacen) ?? Constantes.Almacenes.ALGETE,
                    Cantidad = linea.Cantidad,
                    FechaModificacion = fecha ?? ahora
                };
            }
            List<LineaPedidoPicking> lineasPropias = propias.Where(p => p != null).ToList();
            List<LineaPedidoPicking> todas = grabadas
                .Where(g => !EsDelPedido(g) && !lineasPropias.Any(p => p.Id == g.Id))
                .Concat(lineasPropias)
                .OrderBy(l => l.FechaModificacion).ThenBy(l => l.Id)
                .ToList();

            var candidato = new PedidoPicking { Empresa = empresaPedido, Id = pedido ?? 0, Lineas = lineasPropias };
            GestorReservasStock.Reservar(stocks, new List<PedidoPicking> { candidato }, todas);

            return propias.Select(p => p?.CantidadReservada ?? 0).ToArray();
        }

        private static string Normalizar(string valor) =>
            string.IsNullOrWhiteSpace(valor) ? null : valor.Trim().ToUpperInvariant();
    }
}
