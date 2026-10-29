using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#606: una línea del pedido (o de la plantilla) tal como la necesita el repartidor.</summary>
    public class LineaPedidoFechaEntregaAgencia
    {
        public string Producto { get; set; }
        public string Almacen { get; set; }
        public int Cantidad { get; set; }
        public decimal BaseImponible { get; set; }
        public DateTime? FechaEntrega { get; set; }
        public bool YaEnPicking { get; set; }
        /// <summary>NestoAPI#730: Nº_Orden de la línea (null o 0 si aún no está grabada).</summary>
        public int? Id { get; set; }
        /// <summary>NestoAPI#730: su Fecha_Modificación, que decide su turno en el reparto por antigüedad (null si es nueva).</summary>
        public DateTime? FechaModificacion { get; set; }
    }

    /// <summary>
    /// NestoAPI#606: de dónde sale cada unidad de las líneas de Algete, con el stock leído para la sombra del modo de
    /// servicio (<see cref="DatosSombraModoServicio"/>, pendientes SIN el propio pedido) y el MISMO reparto por causas que
    /// usa el sugeridor (<see cref="ClasificadorCausasModoServicio.ClasificarLinea"/>, #563): libres en Algete → desmontando un
    /// kit libre en Algete que lo lleve (<see cref="BolsaKitsAlgete"/>, 13/10/26) → tiendas →
    /// pedido a proveedor enviado (con su fecha prevista, si no pasa de
    /// <see cref="ClasificadorCausasModoServicio.DIAS_MAXIMOS_EN_CAMINO"/> días) → sobre pedido / sin fecha.
    ///
    /// <para>Lo de las tiendas se reparte entre Reina y Alcobendas según las unidades libres de cada una (stock − sus
    /// pendientes), empezando por la tienda cuya reposición deja salir antes el pedido (la que se lo lleva primero); lo
    /// que las tiendas no tienen libre es lo que ya viene de camino (reposiciones generadas y sin recibir). Varias líneas
    /// del mismo producto se reparten en orden: primero las que ya tienen picking.</para>
    /// </summary>
    public static class RepartidorStockFechaEntregaAgencia
    {
        public static readonly IReadOnlyList<string> TIENDAS = new[] { Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS };

        public static bool EsDeAlgete(string almacen) =>
            string.Equals(almacen?.Trim(), Constantes.Almacenes.ALGETE, StringComparison.OrdinalIgnoreCase);

        /// <param name="tiendasPorOrden">Las tiendas en el orden en que se cogen sus unidades.</param>
        /// <param name="enAlgetePorAntiguedad">NestoAPI#730: las unidades que salen de Algete en cada línea según el reparto por
        /// antigüedad del picking (<see cref="Stocks.IRepartoStockPorAntiguedad"/>). Es lo que usa siempre
        /// <see cref="ServicioFechaEntregaAgencia"/>; sin él (solo los tests del resto del reparto) las libres son stock −
        /// pendientes de los demás, la regla de antes.</param>
        public static List<LineaFechaEntregaAgencia> Repartir(IEnumerable<LineaPedidoFechaEntregaAgencia> lineas, DatosSombraModoServicio datos,
            IEnumerable<string> tiendasPorOrden, DateTime hoy, IReadOnlyDictionary<LineaPedidoFechaEntregaAgencia, int> enAlgetePorAntiguedad = null)
        {
            datos = datos ?? new DatosSombraModoServicio();
            List<string> tiendas = (tiendasPorOrden ?? TIENDAS).ToList();
            var fuente = new FuenteDatosCausasSombra(datos);
            List<LineaPedidoFechaEntregaAgencia> deAlgete = (lineas ?? Enumerable.Empty<LineaPedidoFechaEntregaAgencia>())
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.Producto) && l.Cantidad > 0 && EsDeAlgete(l.Almacen))
                .ToList();
            var repartidas = new Dictionary<LineaPedidoFechaEntregaAgencia, LineaFechaEntregaAgencia>();
            var kits = new BolsaKitsAlgete(datos, deAlgete);

            foreach (IGrouping<string, LineaPedidoFechaEntregaAgencia> grupo in deAlgete.GroupBy(l => ResumenStocksProductos.Clave(l.Producto)))
            {
                string producto = grupo.First().Producto.Trim();
                int cantidad = grupo.Sum(l => l.Cantidad);
                DatosStockCausaModoServicio datosProducto = fuente.Leer(producto, Constantes.Almacenes.ALGETE);
                if (enAlgetePorAntiguedad != null)
                {
                    // NestoAPI#730: las libres de Algete son las que el picking le daría a estas líneas por antigüedad; el resto
                    // de causas (kits, tiendas, proveedor, sobre pedido) se clasifica igual que antes.
                    datosProducto = ConLibresEnAlgete(datosProducto, grupo.Sum(l => EnAlgete(enAlgetePorAntiguedad, l)));
                }
                CausasLineaModoServicio causas = ClasificadorCausasModoServicio.ClasificarLinea(producto, Constantes.Almacenes.ALGETE,
                    cantidad, datosProducto, hoy);

                // NestoAPI#606 (kits, Carlos 13/10/26, pedido 928288): lo que falta en Algete puede salir desmontando un kit que
                // lo lleve y esté libre en Algete, antes que de las tiendas o del proveedor. El resto se vuelve a clasificar sin
                // esas unidades (las libres no cambian: el kit solo cubre lo que faltaba).
                Dictionary<string, int> deKitsGrupo = causas.Faltan > 0 ? kits.Tomar(producto, causas.Faltan) : new Dictionary<string, int>();
                int deKits = deKitsGrupo.Values.Sum();
                if (deKits > 0)
                {
                    causas = ClasificadorCausasModoServicio.ClasificarLinea(producto, Constantes.Almacenes.ALGETE, cantidad - deKits, datosProducto, hoy);
                }
                List<KeyValuePair<string, int>> porKit = deKitsGrupo.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToList();

                int libres = causas.Libres;
                int paraTiendas = causas.Tiendas;
                var porTienda = new List<KeyValuePair<string, int>>();
                foreach (string tienda in tiendas)
                {
                    string clave = ResumenStocksProductos.Clave(producto, tienda);
                    int libresTienda = Math.Max(0, ResumenStocksProductos.Valor(datos.Resumen.StockAlmacen, clave)
                        - ResumenStocksProductos.Valor(datos.Resumen.PendienteEntregarAlmacen, clave));
                    int unidades = Math.Min(paraTiendas, libresTienda);
                    if (unidades > 0)
                    {
                        porTienda.Add(new KeyValuePair<string, int>(tienda, unidades));
                        paraTiendas -= unidades;
                    }
                }
                int enCamino = paraTiendas;
                int delProveedor = causas.EnCamino;
                string motivoSinFecha = causas.SobrePedido > 0
                    ? "es un producto sobre pedido y aún no hay pedido al proveedor que lo cubra"
                    : null;

                foreach (LineaPedidoFechaEntregaAgencia linea in grupo.OrderByDescending(l => l.YaEnPicking))
                {
                    int resto = linea.Cantidad;
                    int Tomar(ref int bolsa)
                    {
                        int tomadas = Math.Min(resto, bolsa);
                        bolsa -= tomadas;
                        resto -= tomadas;
                        return tomadas;
                    }
                    int TomarPropias(int unidades)
                    {
                        return Tomar(ref unidades);
                    }

                    var repartida = new LineaFechaEntregaAgencia
                    {
                        Producto = producto,
                        Cantidad = linea.Cantidad,
                        BaseImponible = linea.BaseImponible,
                        FechaEntrega = linea.FechaEntrega,
                        YaEnPicking = linea.YaEnPicking,
                        EnAlgete = enAlgetePorAntiguedad == null ? Tomar(ref libres) : TomarPropias(EnAlgete(enAlgetePorAntiguedad, linea)),
                        FechaProveedor = causas.FechaPrevista,
                        PedidoProveedor = PedidoProveedor(datos, producto, causas.FechaPrevista),
                        MotivoSinFecha = motivoSinFecha
                    };
                    for (int i = 0; i < porKit.Count && resto > 0; i++)
                    {
                        int bolsa = porKit[i].Value;
                        int unidades = Tomar(ref bolsa);
                        porKit[i] = new KeyValuePair<string, int>(porKit[i].Key, bolsa);
                        if (unidades > 0)
                        {
                            repartida.DeKits[porKit[i].Key] = unidades;
                        }
                    }
                    for (int i = 0; i < porTienda.Count && resto > 0; i++)
                    {
                        int bolsa = porTienda[i].Value;
                        int unidades = Tomar(ref bolsa);
                        porTienda[i] = new KeyValuePair<string, int>(porTienda[i].Key, bolsa);
                        if (unidades > 0)
                        {
                            repartida.EnTiendas[porTienda[i].Key] = unidades;
                        }
                    }
                    repartida.EnCaminoDeTiendas = Tomar(ref enCamino);
                    repartida.DelProveedor = Tomar(ref delProveedor);
                    repartidas[linea] = repartida;
                }
            }

            return deAlgete.Select(l => repartidas[l]).ToList();
        }

        /// <summary>
        /// NestoAPI#606 (kits, Carlos 13/10/26): los kits libres en Algete que se pueden desmontar para cubrir lo que falta.
        /// <list type="bullet">
        /// <item>Cuenta CUALQUIER kit que lleve el producto (no solo las cajas de individuales), aunque al desmontarlo queden
        /// sueltos otros componentes (decisión de Carlos).</item>
        /// <item>Kits libres = stock del kit en Algete − lo que deben del kit los demás pedidos − lo que pide del kit este mismo
        /// pedido (lo mismo que las libres de cualquier producto).</item>
        /// <item>Los demás pedidos van primero: lo que ya deben de un componente por encima de su stock en Algete (como la
        /// «deuda previa» del proveedor en <see cref="ClasificadorCausasModoServicio"/>) se cubre antes con los kits.</item>
        /// <item>Al desmontar un kit salen TODOS sus componentes: lo que sobre de un kit ya desmontado se usa antes de desmontar
        /// otro (dos productos del pedido que van en el mismo kit no desmontan dos).</item>
        /// <item>Entre varios kits, primero el que más unidades del producto lleva (menos kits que desmontar).</item>
        /// </list>
        /// </summary>
        internal class BolsaKitsAlgete
        {
            private readonly Dictionary<string, List<ComponenteKitFechaEntregaAgencia>> componentesPorKit;
            private readonly Dictionary<string, int> kitsLibres = new Dictionary<string, int>();
            private readonly Dictionary<string, int> sueltos = new Dictionary<string, int>();

            public BolsaKitsAlgete(DatosSombraModoServicio datos, IEnumerable<LineaPedidoFechaEntregaAgencia> lineasAlgete)
            {
                List<ComponenteKitFechaEntregaAgencia> filas = (datos.Kits ?? new List<ComponenteKitFechaEntregaAgencia>())
                    .Where(k => k != null && !string.IsNullOrWhiteSpace(k.Kit) && !string.IsNullOrWhiteSpace(k.Componente) && k.Cantidad > 0)
                    .ToList();
                componentesPorKit = filas
                    .GroupBy(k => ResumenStocksProductos.Clave(k.Kit))
                    .ToDictionary(g => g.Key, g => g.ToList());
                List<LineaPedidoFechaEntregaAgencia> lineas = lineasAlgete.ToList();
                foreach (string kit in componentesPorKit.Keys)
                {
                    string clave = ResumenStocksProductos.Clave(kit, Constantes.Almacenes.ALGETE);
                    int delPedido = lineas.Where(l => ResumenStocksProductos.Clave(l.Producto) == kit).Sum(l => l.Cantidad);
                    kitsLibres[kit] = Math.Max(0, ResumenStocksProductos.Valor(datos.Resumen.StockAlmacen, clave)
                        - ResumenStocksProductos.Valor(datos.Resumen.PendienteEntregarAlmacen, clave) - delPedido);
                }
                foreach (string producto in lineas.Select(l => ResumenStocksProductos.Clave(l.Producto)).Distinct())
                {
                    string clave = ResumenStocksProductos.Clave(producto, Constantes.Almacenes.ALGETE);
                    int deudaPrevia = ResumenStocksProductos.Valor(datos.Resumen.PendienteEntregarAlmacen, clave)
                        - ResumenStocksProductos.Valor(datos.Resumen.StockAlmacen, clave);
                    if (deudaPrevia > 0)
                    {
                        _ = Tomar(producto, deudaPrevia);
                    }
                }
            }

            /// <summary>Hasta <paramref name="unidades"/> del producto sacadas de kits: kit → unidades.</summary>
            public Dictionary<string, int> Tomar(string producto, int unidades)
            {
                var tomadas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                string claveProducto = ResumenStocksProductos.Clave(producto);
                List<ComponenteKitFechaEntregaAgencia> conProducto = componentesPorKit.Values
                    .SelectMany(c => c)
                    .Where(c => ResumenStocksProductos.Clave(c.Componente) == claveProducto && ResumenStocksProductos.Clave(c.Kit) != claveProducto)
                    .OrderByDescending(c => c.Cantidad)
                    .ThenBy(c => c.Kit, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                int resto = unidades;

                void Usar(string kit, int disponibles)
                {
                    int usadas = Math.Min(resto, disponibles);
                    if (usadas <= 0)
                    {
                        return;
                    }
                    string claveSuelto = ResumenStocksProductos.Clave(kit, claveProducto);
                    sueltos[claveSuelto] = ResumenStocksProductos.Valor(sueltos, claveSuelto) - usadas;
                    tomadas[kit] = (tomadas.TryGetValue(kit, out int ya) ? ya : 0) + usadas;
                    resto -= usadas;
                }

                // Primero lo que sobró de kits ya desmontados.
                foreach (ComponenteKitFechaEntregaAgencia componente in conProducto)
                {
                    Usar(componente.Kit.Trim(), ResumenStocksProductos.Valor(sueltos, ResumenStocksProductos.Clave(componente.Kit, claveProducto)));
                }
                foreach (ComponenteKitFechaEntregaAgencia componente in conProducto)
                {
                    if (resto <= 0)
                    {
                        break;
                    }
                    string kit = ResumenStocksProductos.Clave(componente.Kit);
                    int libres = ResumenStocksProductos.Valor(kitsLibres, kit);
                    int aDesmontar = Math.Min(libres, (resto + componente.Cantidad - 1) / componente.Cantidad);
                    if (aDesmontar <= 0)
                    {
                        continue;
                    }
                    kitsLibres[kit] = libres - aDesmontar;
                    foreach (ComponenteKitFechaEntregaAgencia otro in componentesPorKit[kit])
                    {
                        string claveSuelto = ResumenStocksProductos.Clave(kit, otro.Componente);
                        sueltos[claveSuelto] = ResumenStocksProductos.Valor(sueltos, claveSuelto) + aDesmontar * otro.Cantidad;
                    }
                    Usar(componente.Kit.Trim(), ResumenStocksProductos.Valor(sueltos, ResumenStocksProductos.Clave(componente.Kit, claveProducto)));
                }
                return tomadas;
            }
        }

        /// <summary>NestoAPI#730: lo que le toca a la línea en Algete por antigüedad (nunca más que su cantidad).</summary>
        private static int EnAlgete(IReadOnlyDictionary<LineaPedidoFechaEntregaAgencia, int> enAlgete, LineaPedidoFechaEntregaAgencia linea)
        {
            return enAlgete.TryGetValue(linea, out int unidades) ? Math.Max(0, Math.Min(linea.Cantidad, unidades)) : 0;
        }

        /// <summary>
        /// NestoAPI#730: los datos del producto con las libres de Algete fijadas a <paramref name="libres"/> (stock = libres, sin
        /// pendientes), para que <see cref="ClasificadorCausasModoServicio.ClasificarLinea"/> las dé tal cual. El disponible de
        /// todas las sedes, lo pedido al proveedor y el estado del producto no cambian.
        /// </summary>
        private static DatosStockCausaModoServicio ConLibresEnAlgete(DatosStockCausaModoServicio datos, int libres)
        {
            datos = datos ?? new DatosStockCausaModoServicio();
            return new DatosStockCausaModoServicio
            {
                StockAlmacen = libres,
                PendientesAlmacen = 0,
                DisponibleTodos = datos.DisponibleTodos,
                EstadoProducto = datos.EstadoProducto,
                PendienteRecibir = datos.PendienteRecibir,
                FechaPrevista = datos.FechaPrevista
            };
        }

        /// <summary>NestoAPI#606 (correo del pedido): el pedido a proveedor de la fecha prevista más temprana de Algete.</summary>
        private static int? PedidoProveedor(DatosSombraModoServicio datos, string producto, DateTime? fechaPrevista)
        {
            return fechaPrevista.HasValue
                && datos.PedidoCompraPrevisto.TryGetValue(ResumenStocksProductos.Clave(producto, Constantes.Almacenes.ALGETE), out int pedido)
                ? pedido
                : (int?)null;
        }
    }
}
