using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Ubicaciones
{
    // ------------------------------------------------------------------------------------------------------------------
    // NestoAPI#594 (corte 2): el CUADRE de Ubicaciones contra el stock de ExtractoProducto, por producto y almacén con
    // Almacenes.ControlUbicaciones = 1.
    //
    // Qué cuenta como mercancía en el almacén: las filas con Estado >= 0.
    //   0  en su hueco, libre
    //   2  en el almacén sin hueco («pendiente de ubicar»: entradas de compra, de reposición, faltas)
    //   3  reservada para una línea de pedido (picking): sigue en la estantería hasta el albarán
    //   4  reservada para una reposición: sigue en la estantería hasta que sale el traspaso (-4)
    //   5  reservada para montar un kit: sigue en la estantería hasta que se monta (-5)
    // Las negativas son HISTÓRICO, lo que ya salió (o ya se ubicó) y que se guarda para la traza: -1 entrada de compra ya
    // ubicada (prdUbicar 2 → -1) o devolución a proveedor, -2 salida por diario, -3 albarán de venta, -4 salida de
    // reposición, -5 componente de kit montado, -102 regularización de inventario. No están en la estantería.
    //
    // Es el mismo criterio que ya usaba la base de datos: prdQuedaDescuadradaUbicación (Carlos, 05/02/10; hoy desactivado
    // con un «return 0» al principio) sumaba Ubicaciones con «estado>=0» de las empresas 1 y 3 en ALG contra la suma de
    // ExtractoProducto de las mismas empresas y almacén, y se saltaba los productos con Productos.Ubicar = 0; y las
    // limpiezas de prdContabilizarInventario borran «cantidad=0 and estado>=0» («las que sean positivas»).
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Lo que dice ExtractoProducto de un producto en un almacén (empresa y su espejo juntas).</summary>
    public class StockExtractoProducto
    {
        public string Producto { get; set; }
        public int Stock { get; set; }
    }

    /// <summary>La suma de Ubicaciones de un producto en un almacén para un estado (empresa y su espejo juntas).</summary>
    public class UbicacionesPorEstado
    {
        public string Producto { get; set; }
        public int Estado { get; set; }
        public int Cantidad { get; set; }
        public int Filas { get; set; }
    }

    /// <summary>Lo que se sabe del producto para el cuadre (de Productos).</summary>
    public class ProductoCuadre
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>Productos.Ubicar: si es false, el producto no se ubica (no tiene filas en Ubicaciones y no descuadra por eso).</summary>
        public bool Ubicar { get; set; }
    }

    public class CuadreUbicacionProducto
    {
        public string Almacen { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public bool Ubicar { get; set; }
        /// <summary>SUM(ExtractoProducto.Cantidad) del producto en el almacén.</summary>
        public int Stock { get; set; }
        /// <summary>Estado 0.</summary>
        public int EnHueco { get; set; }
        /// <summary>Estado 2.</summary>
        public int PendienteDeUbicar { get; set; }
        /// <summary>Estado 3.</summary>
        public int ReservadoPicking { get; set; }
        /// <summary>Estado 4.</summary>
        public int ReservadoReposicion { get; set; }
        /// <summary>Estado 5.</summary>
        public int ReservadoKit { get; set; }
        /// <summary>Otros estados positivos que no conocemos (no debería haber): cuentan, pero se enseñan aparte.</summary>
        public int OtrosEstados { get; set; }
        /// <summary>Todo lo que Ubicaciones dice que hay en el almacén (estados &gt;= 0).</summary>
        public int Ubicaciones { get; set; }
        /// <summary>Ubicaciones − Stock. Positivo: sobra en Ubicaciones (hay huecos con mercancía que el extracto ya no tiene). Negativo: falta.</summary>
        public int Diferencia { get; set; }
        public bool Cuadra { get; set; }
        /// <summary>«Cuadra», «Sobra en ubicaciones», «Falta en ubicaciones», «Sin ubicaciones».</summary>
        public string Situacion { get; set; }
    }

    /// <summary>
    /// NestoAPI#594 (corte 2): el cuadre, sin base de datos. Recibe lo agregado por el repositorio y decide por producto.
    /// </summary>
    public static class CuadreUbicaciones
    {
        public const string CUADRA = "Cuadra";
        public const string SOBRA_EN_UBICACIONES = "Sobra en ubicaciones";
        public const string FALTA_EN_UBICACIONES = "Falta en ubicaciones";
        public const string SIN_UBICACIONES = "Sin ubicaciones";

        /// <summary>Lo que es mercancía física en el almacén (ver la cabecera del fichero): Estado &gt;= 0.</summary>
        public static bool CuentaComoMercancia(int estado) => estado >= 0;

        /// <summary>
        /// Un resultado por producto que aparezca en el extracto o en Ubicaciones. Los productos con Productos.Ubicar = 0 sin
        /// ninguna fila en Ubicaciones cuadran siempre (no se ubican); si tienen filas, se comparan como los demás.
        /// </summary>
        /// <param name="soloDescuadrados">true: solo los que no cuadran (la consulta del almacén entero).</param>
        public static List<CuadreUbicacionProducto> Cuadrar(string almacen, IEnumerable<StockExtractoProducto> stocks,
            IEnumerable<UbicacionesPorEstado> ubicaciones, IEnumerable<ProductoCuadre> productos, bool soloDescuadrados)
        {
            var comparador = StringComparer.OrdinalIgnoreCase;
            Dictionary<string, int> stockPorProducto = (stocks ?? Enumerable.Empty<StockExtractoProducto>())
                .Where(s => !string.IsNullOrWhiteSpace(s?.Producto))
                .GroupBy(s => s.Producto.Trim(), comparador)
                .ToDictionary(g => g.Key, g => g.Sum(s => s.Stock), comparador);
            Dictionary<string, List<UbicacionesPorEstado>> ubicacionesPorProducto = (ubicaciones ?? Enumerable.Empty<UbicacionesPorEstado>())
                .Where(u => !string.IsNullOrWhiteSpace(u?.Producto))
                .GroupBy(u => u.Producto.Trim(), comparador)
                .ToDictionary(g => g.Key, g => g.ToList(), comparador);
            Dictionary<string, ProductoCuadre> fichas = (productos ?? Enumerable.Empty<ProductoCuadre>())
                .Where(p => !string.IsNullOrWhiteSpace(p?.Producto))
                .GroupBy(p => p.Producto.Trim(), comparador)
                .ToDictionary(g => g.Key, g => g.First(), comparador);

            var resultado = new List<CuadreUbicacionProducto>();
            foreach (string producto in stockPorProducto.Keys.Union(ubicacionesPorProducto.Keys, comparador).OrderBy(p => p, comparador))
            {
                _ = fichas.TryGetValue(producto, out ProductoCuadre ficha);
                _ = stockPorProducto.TryGetValue(producto, out int stock);
                List<UbicacionesPorEstado> filas = ubicacionesPorProducto.TryGetValue(producto, out List<UbicacionesPorEstado> f)
                    ? f.Where(u => CuentaComoMercancia(u.Estado)).ToList()
                    : new List<UbicacionesPorEstado>();

                var cuadre = new CuadreUbicacionProducto
                {
                    Almacen = almacen?.Trim(),
                    Producto = producto,
                    Nombre = ficha?.Nombre?.Trim(),
                    // Sin ficha (no debería): se trata como ubicable, para que se vea
                    Ubicar = ficha?.Ubicar ?? true,
                    Stock = stock,
                    EnHueco = Suma(filas, EstadosUbicacion.LIBRE),
                    PendienteDeUbicar = Suma(filas, EstadosUbicacion.PENDIENTE_DE_UBICAR),
                    ReservadoPicking = Suma(filas, EstadosUbicacion.RESERVA_PICKING),
                    ReservadoReposicion = Suma(filas, EstadosUbicacion.RESERVA_REPOSICION),
                    ReservadoKit = Suma(filas, EstadosUbicacion.RESERVA_KIT),
                    OtrosEstados = filas.Where(u => !ESTADOS_CONOCIDOS.Contains(u.Estado)).Sum(u => u.Cantidad),
                    Ubicaciones = filas.Sum(u => u.Cantidad)
                };
                cuadre.Diferencia = cuadre.Ubicaciones - cuadre.Stock;
                bool sinFilas = filas.Count == 0;
                cuadre.Cuadra = cuadre.Diferencia == 0 || (!cuadre.Ubicar && sinFilas);
                cuadre.Situacion = cuadre.Cuadra ? CUADRA
                    : sinFilas ? SIN_UBICACIONES
                    : cuadre.Diferencia > 0 ? SOBRA_EN_UBICACIONES
                    : FALTA_EN_UBICACIONES;

                if (!soloDescuadrados || !cuadre.Cuadra)
                {
                    resultado.Add(cuadre);
                }
            }
            return resultado;
        }

        private static readonly HashSet<int> ESTADOS_CONOCIDOS = new HashSet<int>
        {
            EstadosUbicacion.LIBRE, EstadosUbicacion.PENDIENTE_DE_UBICAR, EstadosUbicacion.RESERVA_PICKING,
            EstadosUbicacion.RESERVA_REPOSICION, EstadosUbicacion.RESERVA_KIT
        };

        private static int Suma(IEnumerable<UbicacionesPorEstado> filas, int estado) => filas.Where(u => u.Estado == estado).Sum(u => u.Cantidad);
    }
}
