using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.Productos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;

namespace NestoAPI.Infraestructure.Productos
{
    /// <summary>Lo que hace falta saber de un producto para decidir su cambio de estado (lo carga el repositorio).</summary>
    public class DatosEstadoProducto
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public short? Estado { get; set; }
        /// <summary>Stock por almacén (ALG, REI, ALC).</summary>
        public Dictionary<string, int> Stock { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public int PendienteRecibir { get; set; }
        public List<PedidoPendienteEstadoDTO> PedidosPendientes { get; set; } = new List<PedidoPendienteEstadoDTO>();
        public List<ControlStockActual> Controles { get; set; } = new List<ControlStockActual>();

        public int StockEn(string almacen) => Stock != null && Stock.TryGetValue(almacen, out int s) ? s : 0;
    }

    public class ControlStockActual
    {
        public string Almacen { get; set; }
        public int Minimo { get; set; }
        public int Maximo { get; set; }
    }

    /// <summary>Qué se hace con un producto: omitirlo (y por qué) o cambiarlo, quitando o creando controles de stock.</summary>
    public class DecisionEstadoProducto
    {
        public string Producto { get; set; }
        public short? EstadoAnterior { get; set; }
        public short EstadoNuevo { get; set; }
        public bool Omitir => Motivo != null;
        public string Motivo { get; set; }
        /// <summary>Sale de Disponible con controles de stock (máximo o mínimo distinto de 0) en estos almacenes.</summary>
        public List<string> AlmacenesConControles { get; set; } = new List<string>();
        /// <summary>Los controles que se ponen a 0 al aplicar (solo si se ha dicho que se quiten).</summary>
        public List<string> ControlesAQuitar { get; set; } = new List<string>();
        /// <summary>Los controles que se crean al volver a Disponible (solo los sugeridos con máximo &gt; 0).</summary>
        public List<ControlStockSugeridoDTO> ControlesACrear { get; set; } = new List<ControlStockSugeridoDTO>();
        public bool VuelveADisponibleSinControles { get; set; }
        public bool QuedaStockOPendiente { get; set; }
        public bool TienePedidosPendientes { get; set; }
        public List<string> Avisos { get; set; } = new List<string>();
    }

    /// <summary>
    /// NestoAPI#738 (sugerencia 793, Manuel): las reglas del cambio de estado de los productos, puras (sin base de datos),
    /// con las decisiones de Carlos del 29/10/26:
    /// <list type="bullet">
    /// <item>Solo Compras y Dirección (como el cambio de subgrupo en trgProductosUpd).</item>
    /// <item>No se sale de Disponible (0) con controles de stock: o se quitan (máximo y mínimo a 0) o no se cambia el estado. La
    /// pantalla pregunta UNA vez por lote; sin el «sí», el producto se omite con su motivo.</item>
    /// <item>Al volver a Disponible se crean los controles con los máximos y mínimos SUGERIDOS (el motor de las propuestas de
    /// stock en ALG y la media de 24 meses en las tiendas), no los por defecto del almacén. Si no hay ninguno con máximo, el
    /// producto vuelve igual, sin controles, con el aviso de revisar los máximos.</item>
    /// <item>Antes de aplicar: los pedidos de clientes pendientes de servir; al pasar a Extinguir, Anular o nulo, el stock que
    /// queda y lo pendiente de recibir del proveedor.</item>
    /// </list>
    /// </summary>
    public static class ReglasEstadoProducto
    {
        public const short DISPONIBLE = 0;
        public const int MAXIMO_PRODUCTOS_POR_LOTE = 2000;

        public const string MENSAJE_SIN_PERMISO = "Solo Compras y Dirección pueden cambiar el estado de los productos.";
        public const string AVISO_SIN_CONTROLES = "Sin controles de stock: revise los máximos.";
        public const string MOTIVO_NO_EXISTE = "El producto no existe.";

        internal static readonly string[] GRUPOS_QUE_PUEDEN_CAMBIAR =
        {
            Constantes.GruposSeguridad.COMPRAS,
            Constantes.GruposSeguridad.DIRECCION
        };

        private static readonly string[] ORDEN_ALMACENES = { Constantes.Almacenes.ALGETE, Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS };

        public static bool PuedeCambiar(IPrincipal usuario)
        {
            return usuario != null && GRUPOS_QUE_PUEDEN_CAMBIAR.Any(g => usuario.IsInRoleSinDominio(g));
        }

        public static bool EsEstadoValido(short estado, IEnumerable<EstadoProductoDTO> estados)
        {
            return estados != null && estados.Any(e => e.Estado == estado);
        }

        public static string MensajeEstadoNoValido(short estado) => $"El estado {estado} no existe en la tabla de estados de los productos.";

        /// <summary>Estaba en Disponible y deja de estarlo. Un estado nulo (NULL) no es Disponible.</summary>
        public static bool SaleDeDisponible(short? actual, short nuevo) => actual == DISPONIBLE && nuevo != DISPONIBLE;

        public static bool VuelveADisponible(short? actual, short nuevo) => actual != DISPONIBLE && nuevo == DISPONIBLE;

        /// <summary>Al pasar a Extinguir, Anular o a un estado nulo (negativo) importa lo que queda en el almacén y lo que viene.</summary>
        public static bool DejaDeTrabajarse(short nuevo, EstadoProductoDTO infoNuevo)
        {
            return nuevo < 0 || (infoNuevo != null && (infoNuevo.AExtinguir || infoNuevo.AAnular));
        }

        /// <summary>Un control «cuenta» si tiene máximo o mínimo (el trigger mira el máximo; los de 0/0 son controles ya quitados).</summary>
        public static List<string> AlmacenesConControles(DatosEstadoProducto datos)
        {
            return (datos?.Controles ?? new List<ControlStockActual>())
                .Where(c => c.Maximo != 0 || c.Minimo != 0)
                .Select(c => c.Almacen?.Trim())
                .Where(a => !string.IsNullOrEmpty(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => Array.IndexOf(ORDEN_ALMACENES, a) < 0 ? int.MaxValue : Array.IndexOf(ORDEN_ALMACENES, a))
                .ThenBy(a => a)
                .ToList();
        }

        /// <summary>
        /// Decide qué se hace con un producto. <paramref name="datos"/> null = el producto no existe. <paramref name="sugeridos"/>
        /// solo se mira al volver a Disponible.
        /// </summary>
        public static DecisionEstadoProducto Decidir(string producto, DatosEstadoProducto datos, short estadoNuevo, EstadoProductoDTO infoNuevo,
            bool quitarControlesStock, IList<ControlStockSugeridoDTO> sugeridos)
        {
            DecisionEstadoProducto decision = new DecisionEstadoProducto
            {
                Producto = producto?.Trim(),
                EstadoAnterior = datos?.Estado,
                EstadoNuevo = estadoNuevo
            };
            if (datos == null)
            {
                decision.Motivo = MOTIVO_NO_EXISTE;
                return decision;
            }
            if (datos.Estado == estadoNuevo)
            {
                decision.Motivo = $"Ya está en estado {estadoNuevo}.";
                return decision;
            }

            if (datos.PedidosPendientes != null && datos.PedidosPendientes.Count > 0)
            {
                decision.TienePedidosPendientes = true;
                int pedidos = datos.PedidosPendientes.Select(p => p.Pedido).Distinct().Count();
                int unidades = datos.PedidosPendientes.Sum(p => p.Cantidad);
                decision.Avisos.Add(pedidos == 1
                    ? $"Hay 1 pedido de cliente pendiente de servir con este producto ({Unidades(unidades)})."
                    : $"Hay {pedidos} pedidos de clientes pendientes de servir con este producto ({Unidades(unidades)}).");
            }

            if (SaleDeDisponible(datos.Estado, estadoNuevo))
            {
                decision.AlmacenesConControles = AlmacenesConControles(datos);
                if (decision.AlmacenesConControles.Count > 0)
                {
                    if (!quitarControlesStock)
                    {
                        decision.Motivo = $"Tiene controles de stock en {Lista(decision.AlmacenesConControles)}.";
                        return decision;
                    }
                    decision.ControlesAQuitar = decision.AlmacenesConControles.ToList();
                    decision.Avisos.Add($"Se quitarán sus controles de stock en {Lista(decision.ControlesAQuitar)}.");
                }
            }

            if (DejaDeTrabajarse(estadoNuevo, infoNuevo))
            {
                List<string> conStock = ORDEN_ALMACENES.Where(a => datos.StockEn(a) != 0).Select(a => $"{a} {datos.StockEn(a)}").ToList();
                if (conStock.Count > 0)
                {
                    decision.Avisos.Add($"Le queda stock: {string.Join(", ", conStock)}.");
                }
                if (datos.PendienteRecibir > 0)
                {
                    decision.Avisos.Add($"Tiene {Unidades(datos.PendienteRecibir)} pendientes de recibir del proveedor.");
                }
                decision.QuedaStockOPendiente = conStock.Count > 0 || datos.PendienteRecibir > 0;
            }

            if (VuelveADisponible(datos.Estado, estadoNuevo))
            {
                decision.ControlesACrear = (sugeridos ?? new List<ControlStockSugeridoDTO>())
                    .Where(s => s != null && s.StockMaximo > 0)
                    .ToList();
                if (decision.ControlesACrear.Count == 0)
                {
                    decision.VuelveADisponibleSinControles = true;
                    decision.Avisos.Add(AVISO_SIN_CONTROLES);
                }
                else
                {
                    decision.Avisos.Add("Se crearán sus controles de stock con los máximos sugeridos: " +
                        string.Join(", ", decision.ControlesACrear.Select(c => c.StockMinimo > 0
                            ? $"{c.Almacen} máx. {c.StockMaximo} / mín. {c.StockMinimo}"
                            : $"{c.Almacen} máx. {c.StockMaximo}")) + ".");
                }
            }
            return decision;
        }

        /// <summary>Los avisos del lote entero (vista previa), con el número de productos de cada caso.</summary>
        public static List<string> AvisosDelLote(IList<DecisionEstadoProducto> decisiones)
        {
            List<string> avisos = new List<string>();
            int conControles = decisiones.Count(d => d.AlmacenesConControles.Count > 0);
            if (conControles > 0)
            {
                avisos.Add($"{Productos(conControles, "tiene", "tienen")} controles de stock: si no los quita, no se le{(conControles == 1 ? "" : "s")} cambiará el estado.");
            }
            int conPedidos = decisiones.Count(d => d.TienePedidosPendientes);
            if (conPedidos > 0)
            {
                avisos.Add($"{Productos(conPedidos, "tiene", "tienen")} pedidos de clientes pendientes de servir.");
            }
            int conStock = decisiones.Count(d => d.QuedaStockOPendiente);
            if (conStock > 0)
            {
                avisos.Add($"{Productos(conStock, "se queda", "se quedan")} con stock o con compras pendientes de recibir del proveedor.");
            }
            int sinControles = decisiones.Count(d => d.VuelveADisponibleSinControles);
            if (sinControles > 0)
            {
                avisos.Add($"{Productos(sinControles, "vuelve", "vuelven")} a Disponible sin controles de stock: revise los máximos.");
            }
            int omitidos = decisiones.Count(d => d.Omitir && d.AlmacenesConControles.Count == 0);
            if (omitidos > 0)
            {
                avisos.Add(omitidos == 1
                    ? "1 producto no se cambiará (ya está en ese estado o no existe)."
                    : $"{omitidos} productos no se cambiarán (ya están en ese estado o no existen).");
            }
            return avisos;
        }

        /// <summary>La pregunta de una vez por lote: «tiene controles de stock en ALG/REI/ALC: ¿los quito y cambio el estado, o no cambio el estado?».</summary>
        public static string PreguntaControlesStock(IList<DecisionEstadoProducto> decisiones)
        {
            List<DecisionEstadoProducto> con = decisiones.Where(d => d.AlmacenesConControles.Count > 0).ToList();
            if (con.Count == 0)
            {
                return null;
            }
            List<string> almacenes = con.SelectMany(d => d.AlmacenesConControles).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => Array.IndexOf(ORDEN_ALMACENES, a) < 0 ? int.MaxValue : Array.IndexOf(ORDEN_ALMACENES, a)).ToList();
            return con.Count == 1
                ? $"1 producto tiene controles de stock en {Lista(almacenes)}. ¿Los quito y cambio el estado, o no cambio el estado de ese producto?"
                : $"{con.Count} productos tienen controles de stock en {Lista(almacenes)}. ¿Los quito y cambio el estado, o no cambio el estado de esos productos?";
        }

        /// <summary>«ALG», «ALG y REI», «ALG, REI y ALC».</summary>
        internal static string Lista(IList<string> elementos)
        {
            if (elementos == null || elementos.Count == 0)
            {
                return string.Empty;
            }
            if (elementos.Count == 1)
            {
                return elementos[0];
            }
            return string.Join(", ", elementos.Take(elementos.Count - 1)) + " y " + elementos[elementos.Count - 1];
        }

        private static string Unidades(int n) => n == 1 ? "1 ud" : $"{n} uds";

        private static string Productos(int n, string singular, string plural) => n == 1 ? $"1 producto {singular}" : $"{n} productos {plural}";
    }
}
