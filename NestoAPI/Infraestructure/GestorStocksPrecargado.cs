using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// NestoAPI#517: los datos de stock de un conjunto de productos, leídos con unas pocas consultas
    /// agrupadas (<see cref="IServicioGestorStocks.LeerResumenStocks"/>) en vez de 2-5 consultas por producto.
    /// Claves normalizadas como las compara SQL Server: sin el relleno del char y sin mayúsculas.
    /// </summary>
    public class ResumenStocksProductos
    {
        /// <summary>Stock en cada almacén (clave producto|almacén).</summary>
        public Dictionary<string, int> StockAlmacen { get; } = new Dictionary<string, int>();
        /// <summary>Pendiente de entregar en cada almacén (clave producto|almacén).</summary>
        public Dictionary<string, int> PendienteEntregarAlmacen { get; } = new Dictionary<string, int>();
        /// <summary>Pendiente de entregar en todos los almacenes (clave producto).</summary>
        public Dictionary<string, int> PendienteEntregarTotal { get; } = new Dictionary<string, int>();
        /// <summary>Stock en las sedes (clave producto).</summary>
        public Dictionary<string, int> StockSedes { get; } = new Dictionary<string, int>();
        /// <summary>Pendiente de reposición por traspaso (clave producto).</summary>
        public Dictionary<string, int> PendienteReposicion { get; } = new Dictionary<string, int>();

        public static string Clave(string producto) => producto?.Trim().ToUpperInvariant() ?? string.Empty;
        public static string Clave(string producto, string almacen) => Clave(producto) + "|" + Clave(almacen);

        public static void Sumar(Dictionary<string, int> diccionario, string clave, int cantidad)
        {
            diccionario[clave] = (diccionario.TryGetValue(clave, out int actual) ? actual : 0) + cantidad;
        }

        public static int Valor(Dictionary<string, int> diccionario, string clave)
            => diccionario.TryGetValue(clave, out int valor) ? valor : 0;
    }

    /// <summary>
    /// NestoAPI#517: <see cref="IGestorStocks"/> para UN pedido que se está montando, con el stock de todos
    /// sus productos leído de golpe. <see cref="ColorStock(string, string, int)"/> aplica exactamente la
    /// misma regla que <see cref="GestorStocks.ColorStock(string, string, int)"/>, pero en memoria. El resto
    /// de métodos (y los productos que no se precargaron) van al gestor real.
    /// </summary>
    public class GestorStocksPrecargado : IGestorStocks
    {
        private readonly IGestorStocks _real;
        private readonly ResumenStocksProductos _resumen;
        private readonly HashSet<string> _precargados;

        public GestorStocksPrecargado(IGestorStocks real, ResumenStocksProductos resumen, IEnumerable<string> productosPrecargados)
        {
            _real = real ?? throw new ArgumentNullException(nameof(real));
            _resumen = resumen ?? throw new ArgumentNullException(nameof(resumen));
            _precargados = new HashSet<string>();
            foreach (string producto in productosPrecargados ?? new string[0])
            {
                _ = _precargados.Add(ResumenStocksProductos.Clave(producto));
            }
        }

        public string ColorStock(string producto, string almacen, int cantidad)
        {
            string claveProducto = ResumenStocksProductos.Clave(producto);
            if (!_precargados.Contains(claveProducto))
            {
                return _real.ColorStock(producto, almacen, cantidad);
            }
            string claveAlmacen = ResumenStocksProductos.Clave(producto, almacen);
            int stockAlmacen = ResumenStocksProductos.Valor(_resumen.StockAlmacen, claveAlmacen);
            int pendientesEntregar = ResumenStocksProductos.Valor(_resumen.PendienteEntregarAlmacen, claveAlmacen);
            if (stockAlmacen - pendientesEntregar - cantidad >= 0)
            {
                return "green";
            }
            int disponibleTodos = ResumenStocksProductos.Valor(_resumen.StockSedes, claveProducto)
                - ResumenStocksProductos.Valor(_resumen.PendienteEntregarTotal, claveProducto)
                + ResumenStocksProductos.Valor(_resumen.PendienteReposicion, claveProducto);
            return disponibleTodos - cantidad >= 0 ? "DeepPink" : "red";
        }

        public string ColorStock(string producto, string almacen) => ColorStock(producto, almacen, 0);

        public bool HayStockDisponibleDeTodo(PedidoVentaDTO pedido) => _real.HayStockDisponibleDeTodo(pedido);
        public bool HayStockDisponibleDeTodo(PedidoVentaDTO pedido, string almacen) => _real.HayStockDisponibleDeTodo(pedido, almacen);
        public int Stock(string producto) => _real.Stock(producto);
        public int Stock(string producto, string almacen) => _real.Stock(producto, almacen);
        public int UnidadesDisponiblesTodosLosAlmacenes(string producto) => _real.UnidadesDisponiblesTodosLosAlmacenes(producto);
        public int UnidadesPendientesEntregar(string producto) => _real.UnidadesPendientesEntregar(producto);
        public int UnidadesPendientesEntregarAlmacen(string producto, string almacen) => _real.UnidadesPendientesEntregarAlmacen(producto, almacen);
    }
}
