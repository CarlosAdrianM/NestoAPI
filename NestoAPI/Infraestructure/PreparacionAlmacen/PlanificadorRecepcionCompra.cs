using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// Una línea de producto de un pedido de compra todavía por recibir (estado 1) o que se dio por no servida hace
    /// poco (-99, proveedor sin control de pendientes) y aún se puede recuperar si llega.
    /// </summary>
    public class LineaCompraPendiente
    {
        public short Estado { get; set; } = 1;
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
        /// <summary>Mañana, como prdInsertarLineaCmp (dateadd(d,1,hoy)), para que el albarán de hoy no se lo lleve.</summary>
        public DateTime FechaResto { get; set; }
        /// <summary>El visto bueno con el que queda lo recibido.</summary>
        public bool VistoBueno { get; set; }
        /// <summary>Era una línea en -99 (dada por no servida) que vuelve a estado 1 para recibirse con su pedido.</summary>
        public bool Reactivada { get; set; }
    }

    /// <summary>Lo que ha llegado de una línea que se había dado por no servida: para decírselo a quien recibe.</summary>
    public class LineaRecuperada
    {
        public int Pedido { get; set; }
        public int NumeroOrden { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public DateTime FechaNoServido { get; set; }
        public string Texto => $"{Cantidad} ud. de {Producto} eran del pedido {Pedido}, que se dio por no servido el " +
            $"{FechaNoServido:dd/MM}: entran con ese pedido.";
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
        /// NºOrden de las demás líneas en curso de los pedidos que se albaranean hoy: pasan a mañana, igual que hace
        /// prdInsertarLineaCmp (fecharecepción = dateadd(d,1,hoy) para el resto de líneas en curso del pedido). Así
        /// prdCrearAlbaránCmp, que solo coge fecharecepción &lt;= hoy, no se las lleva. El visto bueno no se toca.
        /// </summary>
        public List<int> Aplazadas { get; } = new List<int>();
        public DateTime FechaAplazadas { get; set; }
        public List<ExcesoRecepcion> Excesos { get; } = new List<ExcesoRecepcion>();
        public List<ProductoNoPedido> NoPedidos { get; } = new List<ProductoNoPedido>();
        /// <summary>Lo que llega de líneas -99 recientes: entra con su pedido (no es exceso ni pide aprobación).</summary>
        public List<LineaRecuperada> Recuperadas { get; } = new List<LineaRecuperada>();
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
        /// <summary>Hasta cuántos días atrás (por FechaRecepción) se recupera una línea -99.</summary>
        public const int DIAS_RECUPERABLE = 30;

        public static PlanRecepcionCompra Planificar(IEnumerable<LineaCompraPendiente> lineas, IDictionary<string, int> lecturas,
            DateTime hoy, bool esCompras)
        {
            hoy = hoy.Date;
            var plan = new PlanRecepcionCompra { FechaAplazadas = hoy.AddDays(1) };

            // Del pedido más antiguo al más reciente; dentro del pedido, en el orden de sus líneas
            List<LineaCompraPendiente> validas = (lineas ?? Enumerable.Empty<LineaCompraPendiente>())
                .Where(l => l != null && l.Cantidad > 0 && !string.IsNullOrWhiteSpace(l.Producto))
                .ToList();
            List<LineaCompraPendiente> ordenadas = validas
                .Where(l => l.Estado == ESTADO_PENDIENTE)
                .OrderBy(l => l.FechaPedido).ThenBy(l => l.Pedido).ThenBy(l => l.NumeroOrden)
                .ToList();
            // Decisión de Carlos: sin control de pendientes, lo que se dio por no servido (-99) hace poco se recupera si
            // llega, ANTES de tratarlo como exceso o no pedido; la más antigua primero
            List<LineaCompraPendiente> recuperables = validas
                .Where(l => l.Estado == ESTADO_ANULADA && !l.ControlPendientes && l.FechaRecepcion.Date >= hoy.AddDays(-DIAS_RECUPERABLE))
                .OrderBy(l => l.FechaRecepcion).ThenBy(l => l.Pedido).ThenBy(l => l.NumeroOrden)
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
                List<LineaCompraPendiente> delProducto = ordenadas.Where(l => Normalizar(l.Producto) == lectura.Key)
                    .Concat(recuperables.Where(l => Normalizar(l.Producto) == lectura.Key))
                    .ToList();
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

            List<LineaCompraPendiente> recuperadas = recuperables.Where(l => recibidoPorLinea.ContainsKey(l.NumeroOrden)).ToList();
            foreach (LineaCompraPendiente linea in recuperadas)
            {
                plan.Recuperadas.Add(new LineaRecuperada
                {
                    Pedido = linea.Pedido,
                    NumeroOrden = linea.NumeroOrden,
                    Producto = linea.Producto?.Trim(),
                    Cantidad = recibidoPorLinea[linea.NumeroOrden],
                    FechaNoServido = linea.FechaRecepcion.Date
                });
            }

            List<LineaCompraPendiente> recibibles = ordenadas.Concat(recuperadas).ToList();
            var pedidosConAlgoRecibido = new HashSet<int>(recibibles.Where(l => recibidoPorLinea.ContainsKey(l.NumeroOrden)).Select(l => l.Pedido));
            var pedidosAAlbaranear = new HashSet<int>();

            foreach (LineaCompraPendiente linea in recibibles.Where(l => pedidosConAlgoRecibido.Contains(l.Pedido)))
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
                        FechaResto = plan.FechaAplazadas,
                        VistoBueno = vistoBueno,
                        Reactivada = linea.Estado == ESTADO_ANULADA
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

                // Sin control de pendientes, lo que se esperaba hasta hoy y no llega pasa a -99 (los -99 históricos son
                // de líneas esperadas hasta el día del albarán: 816 de 817 desde 2020). Lo demás pasa a mañana.
                if (!linea.ControlPendientes && linea.FechaRecepcion.Date <= hoy)
                {
                    plan.Anuladas.Add(linea.NumeroOrden);
                }
                else
                {
                    plan.Aplazadas.Add(linea.NumeroOrden);
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
