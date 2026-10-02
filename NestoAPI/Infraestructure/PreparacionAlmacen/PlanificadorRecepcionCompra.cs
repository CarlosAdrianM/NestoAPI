using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Una línea de producto de un pedido de compra todavía por recibir (estado 1).</summary>
    public class LineaCompraPendiente
    {
        public int Pedido { get; set; }
        public DateTime FechaPedido { get; set; }
        /// <summary>LinPedidoCmp.NºOrden.</summary>
        public int NumeroOrden { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public DateTime FechaRecepcion { get; set; }
        public bool VistoBueno { get; set; }
        /// <summary>Proveedores.ControlPendientes del proveedor y contacto de la línea.</summary>
        public bool ControlPendientes { get; set; }

        // Para recalcular los importes al partir la línea (LineaPedidoCompraDTO)
        public decimal Precio { get; set; }
        public decimal DescuentoProveedor { get; set; }
        public decimal DescuentoProducto { get; set; }
        public decimal Descuento { get; set; }
        public decimal DescuentoPP { get; set; }
        public bool AplicarDto { get; set; }
        public decimal PorcentajeIVA { get; set; }
        public decimal PorcentajeRE { get; set; }
    }

    /// <summary>Una línea de la que ha llegado algo. Si no ha llegado entera, lo que falta va en otra línea (Resto).</summary>
    public class LineaRecibida
    {
        public int Pedido { get; set; }
        public int NumeroOrden { get; set; }
        public int Recibido { get; set; }
        public int Resto { get; set; }
        /// <summary>1 si lo que falta sigue pendiente (proveedor con control de pendientes); -99 si no.</summary>
        public short EstadoResto { get; set; }
        public DateTime FechaResto { get; set; }
        /// <summary>El visto bueno con el que queda lo recibido.</summary>
        public bool VistoBueno { get; set; }
    }

    /// <summary>Unidades de más sobre lo pedido: van en una línea nueva, copia de otra del mismo producto.</summary>
    public class ExcesoRecepcion
    {
        public int Pedido { get; set; }
        public int CopiaDe { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public bool VistoBueno { get; set; }
    }

    public class ProductoNoPedido
    {
        public string Producto { get; set; }
        public int Cantidad { get; set; }
    }

    public class PlanRecepcionCompra
    {
        public List<LineaRecibida> Recibidas { get; } = new List<LineaRecibida>();
        /// <summary>NºOrden de líneas no recibidas que pasan a -99 (proveedor sin control de pendientes).</summary>
        public List<int> Anuladas { get; } = new List<int>();
        /// <summary>
        /// NºOrden de líneas que siguen pendientes y que prdCrearAlbaránCmp se llevaría (estado 1, visto bueno y
        /// fecha de recepción de hoy o antes): se les quita el visto bueno mientras se crea el albarán y se les devuelve.
        /// </summary>
        public List<int> Apartadas { get; } = new List<int>();
        public List<ExcesoRecepcion> Excesos { get; } = new List<ExcesoRecepcion>();
        public List<ProductoNoPedido> NoPedidos { get; } = new List<ProductoNoPedido>();
        public List<int> PedidosAAlbaranear { get; } = new List<int>();
        /// <summary>Lo que tiene que ver alguien de Compras (exceso o líneas sin visto bueno recibidas por almacén).</summary>
        public List<string> AvisosParaCompras { get; } = new List<string>();
        /// <summary>El pedido al que se ha asignado primero cada producto leído (para la evidencia).</summary>
        public Dictionary<string, int> PedidoDeProducto { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// NestoAPI#559: decide, sin tocar la base de datos, qué se hace con cada línea de los pedidos de un proveedor
    /// al terminar una recepción. Lo leído se reparte del pedido más antiguo al más reciente.
    /// </summary>
    public static class PlanificadorRecepcionCompra
    {
        public const short ESTADO_PENDIENTE = 1;
        public const short ESTADO_ANULADA = -99;

        public static PlanRecepcionCompra Planificar(IEnumerable<LineaCompraPendiente> lineas, IDictionary<string, int> lecturas,
            DateTime hoy, bool esCompras)
        {
            var plan = new PlanRecepcionCompra();
            hoy = hoy.Date;

            // Del pedido más antiguo al más reciente; dentro del pedido, en el orden de sus líneas
            List<LineaCompraPendiente> ordenadas = (lineas ?? Enumerable.Empty<LineaCompraPendiente>())
                .Where(l => l != null && l.Cantidad > 0 && !string.IsNullOrWhiteSpace(l.Producto))
                .OrderBy(l => l.FechaPedido).ThenBy(l => l.Pedido).ThenBy(l => l.NumeroOrden)
                .ToList();

            Dictionary<string, int> leido = (lecturas ?? new Dictionary<string, int>())
                .Where(l => !string.IsNullOrWhiteSpace(l.Key))
                .GroupBy(l => Normalizar(l.Key))
                .Select(g => new { Producto = g.Key, Cantidad = g.Sum(x => x.Value) })
                .Where(l => l.Cantidad > 0)
                .ToDictionary(l => l.Producto, l => l.Cantidad);

            var recibidoPorLinea = new Dictionary<int, int>();
            foreach (KeyValuePair<string, int> lectura in leido)
            {
                List<LineaCompraPendiente> delProducto = ordenadas.Where(l => Normalizar(l.Producto) == lectura.Key).ToList();
                if (!delProducto.Any())
                {
                    plan.NoPedidos.Add(new ProductoNoPedido { Producto = lectura.Key, Cantidad = lectura.Value });
                    continue;
                }

                int quedan = lectura.Value;
                foreach (LineaCompraPendiente linea in delProducto)
                {
                    if (quedan == 0)
                    {
                        break;
                    }
                    int toca = Math.Min(quedan, linea.Cantidad);
                    recibidoPorLinea[linea.NumeroOrden] = toca;
                    quedan -= toca;
                    if (!plan.PedidoDeProducto.ContainsKey(lectura.Key))
                    {
                        plan.PedidoDeProducto[lectura.Key] = linea.Pedido;
                    }
                }

                if (quedan > 0)
                {
                    LineaCompraPendiente masReciente = delProducto.Last();
                    plan.Excesos.Add(new ExcesoRecepcion
                    {
                        Pedido = masReciente.Pedido,
                        CopiaDe = masReciente.NumeroOrden,
                        Producto = lectura.Key,
                        Cantidad = quedan,
                        VistoBueno = esCompras
                    });
                    if (!esCompras)
                    {
                        plan.AvisosParaCompras.Add($"Del producto {lectura.Key} han llegado {quedan} unidades más de las pedidas " +
                            $"(pedido {masReciente.Pedido}): están recibidas sin visto bueno, a la espera de Compras.");
                    }
                }
            }

            var pedidosConAlgoRecibido = new HashSet<int>(ordenadas.Where(l => recibidoPorLinea.ContainsKey(l.NumeroOrden)).Select(l => l.Pedido));
            var pedidosAAlbaranear = new HashSet<int>();

            foreach (LineaCompraPendiente linea in ordenadas.Where(l => pedidosConAlgoRecibido.Contains(l.Pedido)))
            {
                if (recibidoPorLinea.TryGetValue(linea.NumeroOrden, out int recibido))
                {
                    // Recibir es aprobar si lo recibe Compras; si no, una línea sin visto bueno sigue sin él
                    bool vistoBueno = linea.VistoBueno || esCompras;
                    int resto = linea.Cantidad - recibido;
                    plan.Recibidas.Add(new LineaRecibida
                    {
                        Pedido = linea.Pedido,
                        NumeroOrden = linea.NumeroOrden,
                        Recibido = recibido,
                        Resto = resto,
                        EstadoResto = linea.ControlPendientes ? ESTADO_PENDIENTE : ESTADO_ANULADA,
                        // Lo que sigue pendiente no puede tener fecha de hoy: el albarán de hoy se lo llevaría
                        FechaResto = linea.FechaRecepcion.Date > hoy ? linea.FechaRecepcion.Date : hoy.AddDays(1),
                        VistoBueno = vistoBueno
                    });
                    if (vistoBueno)
                    {
                        _ = pedidosAAlbaranear.Add(linea.Pedido);
                    }
                    else
                    {
                        plan.AvisosParaCompras.Add($"Del producto {linea.Producto?.Trim()} (pedido {linea.Pedido}) han llegado {recibido} " +
                            "unidades de una línea sin visto bueno: no entran en el albarán hasta que Compras le dé el visto bueno.");
                    }
                    continue;
                }

                bool laEsperabaHoy = linea.FechaRecepcion.Date <= hoy;
                if (!laEsperabaHoy)
                {
                    continue; // Se espera más adelante: ni se anula ni la coge el albarán de hoy
                }
                if (!linea.ControlPendientes)
                {
                    plan.Anuladas.Add(linea.NumeroOrden); // Sin control de pendientes, lo que no llega se vuelve a pedir
                }
                else if (linea.VistoBueno)
                {
                    plan.Apartadas.Add(linea.NumeroOrden);
                }
            }

            foreach (ExcesoRecepcion exceso in plan.Excesos.Where(e => e.VistoBueno))
            {
                _ = pedidosAAlbaranear.Add(exceso.Pedido);
            }

            plan.PedidosAAlbaranear.AddRange(pedidosAAlbaranear.OrderBy(p => p));
            return plan;
        }

        public static string Normalizar(string producto)
        {
            return producto?.Trim().ToUpperInvariant();
        }
    }
}
