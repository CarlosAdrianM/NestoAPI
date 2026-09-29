using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#563: lo que hace falta saber de un producto en un almacén para clasificar sus causas.</summary>
    public class DatosStockCausaModoServicio
    {
        /// <summary>Stock en el almacén de la línea (ExtractoProducto).</summary>
        public int StockAlmacen { get; set; }
        /// <summary>Pendiente de entregar en ese almacén (LinPedidoVta -1..1), SIN el propio pedido.</summary>
        public int PendientesAlmacen { get; set; }
        /// <summary>Lo mismo que <see cref="GestorStocks.UnidadesDisponiblesTodosLosAlmacenes"/>: stock de las sedes
        /// − pendiente de entregar en todas + pendiente de reposición por traspaso.</summary>
        public int DisponibleTodos { get; set; }
        /// <summary>Productos.Estado (1 = sobre pedido). Null si no se ha podido leer.</summary>
        public short? EstadoProducto { get; set; }
        /// <summary>Unidades en pedidos a proveedor ENVIADOS y sin recibir (LinPedidoCmp -1/1, Enviado) para ese almacén.</summary>
        public int PendienteRecibir { get; set; }
        /// <summary>La FechaRecepción más temprana de esos pedidos a proveedor.</summary>
        public DateTime? FechaPrevista { get; set; }
    }

    /// <summary>NestoAPI#563: de dónde saca el clasificador los datos de cada producto y almacén.</summary>
    public interface IFuenteDatosCausasModoServicio
    {
        DatosStockCausaModoServicio Leer(string producto, string almacen);
    }

    /// <summary>NestoAPI#563: por qué una unidad de la línea sale (o no) ahora.</summary>
    public enum CausaModoServicio
    {
        /// <summary>Hay unidades libres (stock − pendientes) en el almacén de la línea: sale ya.</summary>
        Libre,
        /// <summary>Falta en el almacén pero lo hay en las tiendas: «Tras reponer de tiendas».</summary>
        Tiendas,
        /// <summary>Hay un pedido a proveedor enviado con fecha prevista: llega en días.</summary>
        EnCamino,
        /// <summary>Producto «sobre pedido» (estado 1): se pide al proveedor al venderlo; llega en días.</summary>
        SobrePedido,
        /// <summary>Sin stock en ningún sitio y sin nada pedido: no se sabe cuándo llega.</summary>
        SinFecha
    }

    /// <summary>NestoAPI#563: cuántas unidades de la línea (producto + almacén, cantidades sumadas) caen en cada causa.</summary>
    public class CausasLineaModoServicio
    {
        public string Producto { get; set; }
        public string Almacen { get; set; }
        public int Cantidad { get; set; }
        public int Libres { get; set; }
        public int Tiendas { get; set; }
        public int EnCamino { get; set; }
        public int SobrePedido { get; set; }
        public int SinFecha { get; set; }
        public DateTime? FechaPrevista { get; set; }

        public int Faltan => Cantidad - Libres;

        /// <summary>La causa que decide la línea: la peor de las que tiene (sin fecha &gt; sobre pedido &gt; en camino &gt; tiendas &gt; libre).</summary>
        public CausaModoServicio Principal =>
            SinFecha > 0 ? CausaModoServicio.SinFecha
            : SobrePedido > 0 ? CausaModoServicio.SobrePedido
            : EnCamino > 0 ? CausaModoServicio.EnCamino
            : Tiendas > 0 ? CausaModoServicio.Tiendas
            : CausaModoServicio.Libre;

        /// <summary>Texto corto para la tabla de la sombra: «35946@ALG x1: sobre pedido 1».</summary>
        public string Resumen()
        {
            var partes = new List<string>();
            if (Libres > 0) partes.Add($"libres {Libres}");
            if (Tiendas > 0) partes.Add($"tiendas {Tiendas}");
            if (EnCamino > 0) partes.Add($"en camino {EnCamino}{(FechaPrevista.HasValue ? " (" + FechaPrevista.Value.ToString("dd/MM/yy", CultureInfo.InvariantCulture) + ")" : string.Empty)}");
            if (SobrePedido > 0) partes.Add($"sobre pedido {SobrePedido}");
            if (SinFecha > 0) partes.Add($"sin fecha {SinFecha}");
            return $"{Producto}@{Almacen} x{Cantidad}: {string.Join(", ", partes)}";
        }
    }

    /// <summary>NestoAPI#563: la sugerencia del clasificador por causas (misma forma que la de colores, más las causas).</summary>
    public class SugerenciaPorCausas
    {
        public byte Modo { get; set; }
        public string Nombre { get; set; }
        public string Motivo { get; set; }
        public List<byte> ModosPermitidos { get; set; } = new List<byte>();
        public List<ModoServicioPermitidoDTO> Modos { get; set; } = new List<ModoServicioPermitidoDTO>();
        public List<CausasLineaModoServicio> Lineas { get; set; } = new List<CausasLineaModoServicio>();
    }

    /// <summary>
    /// NestoAPI#563 (Carlos, 29/09/26, pedidos 927293 y 927312): el modo de servicio por las CAUSAS del stock y no por
    /// los colores del correo. Un rojo «sobre pedido» (llega en días) y un rojo «agotado sin fecha» no son lo mismo, y
    /// una línea con 1 de 2 unidades también sale roja. Cada unidad de la línea se reparte, por este orden, en:
    /// <list type="number">
    /// <item><b>libres</b> en el almacén de la línea (stock − pendientes de entregar);</item>
    /// <item><b>tiendas</b>: lo que falta y cubre el disponible de todas las sedes (como el rosa del correo);</item>
    /// <item><b>en camino</b>: pedido a proveedor enviado sin recibir (LinPedidoCmp -1/1 con Enviado, como la
    /// «fecha estimada de recepción» de la ficha de producto), descontando lo que ya deben otros pedidos
    /// (disponible de todas las sedes en negativo), y solo si la fecha prevista no pasa de
    /// <see cref="DIAS_MAXIMOS_EN_CAMINO"/> días;</item>
    /// <item><b>sobre pedido</b>: el resto, si el producto está en estado 1 (EstadosProducto.SobrePedido);</item>
    /// <item><b>sin fecha</b>: el resto, en cualquier otro caso.</item>
    /// </list>
    /// Regla del pedido (almacén central; tienda, otros almacenes y pedidos sin productos, igual que
    /// <see cref="SugeridorModoServicio"/>):
    /// <list type="bullet">
    /// <item>No falta nada → «Todo junto» (solo ese).</item>
    /// <item>Algo hay que traerlo de las tiendas → «Tras reponer de tiendas» (todos).</item>
    /// <item>No hay NINGUNA unidad libre (#561) → «Todo junto» (solo ese).</item>
    /// <item>Hay libres y algo sin fecha → «Ahora lo que hay, el resto de una vez» (todos menos «Tras reponer»).</item>
    /// <item>Hay libres y lo que falta llega en días (sobre pedido o en camino) → «Todo junto» (todos menos «Tras reponer»).</item>
    /// </list>
    /// De momento solo se usa en SOMBRA (<see cref="SombraModoServicio"/>): no cambia nada de lo que ve el usuario.
    /// </summary>
    public static class ClasificadorCausasModoServicio
    {
        internal const string MOTIVO_TODO_LIBRE = "Hay stock de todo en el almacén del pedido: sale todo junto.";

        /// <summary>
        /// Un pedido a proveedor solo cuenta como «llega en días» si su fecha prevista no pasa de estos días (hay
        /// pedidos enviados con fecha a meses vista, p. ej. enero de 2027 el 29/09/26). Más lejos, esas unidades son
        /// «sin fecha» (o «sobre pedido» si el producto lo es). Las fechas ya pasadas (retrasos) sí cuentan. A revisar
        /// con Carlos a la vista de la sombra.
        /// </summary>
        public const int DIAS_MAXIMOS_EN_CAMINO = 15;

        /// <summary>El reparto de las unidades de una línea entre las causas. Puro.</summary>
        public static CausasLineaModoServicio ClasificarLinea(string producto, string almacen, int cantidad, DatosStockCausaModoServicio datos,
            DateTime? hoy = null)
        {
            datos = datos ?? new DatosStockCausaModoServicio();
            var causas = new CausasLineaModoServicio
            {
                Producto = producto,
                Almacen = almacen,
                Cantidad = Math.Max(0, cantidad)
            };
            int resto = causas.Cantidad;

            int libresAlmacen = Math.Max(0, datos.StockAlmacen - datos.PendientesAlmacen);
            causas.Libres = Math.Min(resto, libresAlmacen);
            resto -= causas.Libres;

            // El disponible de todas las sedes ya incluye las libres del propio almacén.
            int enTiendas = Math.Max(0, datos.DisponibleTodos - causas.Libres);
            causas.Tiendas = Math.Min(resto, enTiendas);
            resto -= causas.Tiendas;

            // Lo que llega del proveedor lo consumen primero los pedidos que ya esperan (disponible en negativo).
            int deudaPrevia = Math.Max(0, -datos.DisponibleTodos);
            bool llegaPronto = !datos.FechaPrevista.HasValue
                || datos.FechaPrevista.Value.Date <= (hoy ?? DateTime.Today).Date.AddDays(DIAS_MAXIMOS_EN_CAMINO);
            int llegaParaEste = llegaPronto ? Math.Max(0, datos.PendienteRecibir - deudaPrevia) : 0;
            causas.EnCamino = Math.Min(resto, llegaParaEste);
            resto -= causas.EnCamino;
            if (causas.EnCamino > 0)
            {
                causas.FechaPrevista = datos.FechaPrevista;
            }

            if (datos.EstadoProducto == Constantes.Productos.ESTADO_SOBRE_PEDIDO)
            {
                causas.SobrePedido = resto;
            }
            else
            {
                causas.SinFecha = resto;
            }
            return causas;
        }

        /// <summary>La sugerencia por causas del pedido que se está montando (o recién grabado, si la fuente excluye sus líneas).</summary>
        public static SugerenciaPorCausas Sugerir(PedidoVentaDTO pedido, IFuenteDatosCausasModoServicio fuente, DateTime? hoy = null)
        {
            if (fuente == null)
            {
                throw new ArgumentNullException(nameof(fuente));
            }
            List<LineaPedidoVentaDTO> productos = LineasProducto(pedido);
            if (!productos.Any())
            {
                return ConModosSinStock(Constantes.Pedidos.ModosServicio.POR_DEFECTO,
                    "El pedido no tiene líneas de producto: se aplica el modo por defecto.", ModosServicioPermitidos.TipoAlmacen.Otro, false);
            }
            ModosServicioPermitidos.TipoAlmacen tipo = ModosServicioPermitidos.Clasificar(productos);
            if (tipo == ModosServicioPermitidos.TipoAlmacen.Tienda)
            {
                return ConModosSinStock(Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO,
                    "Pedido de tienda: el cliente se lleva lo que hay y el resto se sirve según vaya entrando.", tipo, true);
            }
            if (tipo == ModosServicioPermitidos.TipoAlmacen.Otro)
            {
                return ConModosSinStock(Constantes.Pedidos.ModosServicio.TODO_JUNTO,
                    "Pedido de un almacén sin reglas de stock (por ejemplo Amazon): por defecto sale todo junto.", tipo, true);
            }

            // Como en #515: un grupo por producto y almacén con la cantidad sumada (la línea normal y la de regalo
            // consumen el mismo stock).
            List<CausasLineaModoServicio> lineas = productos
                .GroupBy(l => new { Producto = l.Producto.Trim(), Almacen = l.almacen?.Trim() })
                .Select(g => ClasificarLinea(g.Key.Producto, g.Key.Almacen, g.Sum(l => l.Cantidad), fuente.Leer(g.Key.Producto, g.Key.Almacen), hoy))
                .ToList();
            SugerenciaPorCausas sugerencia = Decidir(lineas);
            sugerencia.Lineas = lineas;
            return sugerencia;
        }

        internal static List<LineaPedidoVentaDTO> LineasProducto(PedidoVentaDTO pedido)
            => (pedido?.Lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>())
                .Where(l => l != null && (l.tipoLinea == null || l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                            && !string.IsNullOrWhiteSpace(l.Producto) && l.Cantidad > 0)
                .ToList();

        /// <summary>El núcleo puro de la regla del almacén central, a partir de las causas de cada línea.</summary>
        internal static SugerenciaPorCausas Decidir(IList<CausasLineaModoServicio> lineas)
        {
            lineas = lineas ?? new List<CausasLineaModoServicio>();
            bool falta = lineas.Any(l => l.Faltan > 0);
            bool hayAhora = lineas.Any(l => l.Libres > 0);
            int conTiendas = lineas.Count(l => l.Tiendas > 0);
            int sinFecha = lineas.Count(l => l.SinFecha > 0);
            List<CausasLineaModoServicio> llegan = lineas.Where(l => l.SinFecha == 0 && (l.SobrePedido > 0 || l.EnCamino > 0)).ToList();

            byte modo;
            string motivo;
            if (!falta)
            {
                modo = Constantes.Pedidos.ModosServicio.TODO_JUNTO;
                motivo = MOTIVO_TODO_LIBRE;
            }
            else if (conTiendas > 0)
            {
                modo = Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS;
                motivo = $"{conTiendas} línea{Plural(conTiendas)} hay que traerla{Plural(conTiendas)} de las tiendas: se espera a la reposición.";
            }
            else if (!hayAhora)
            {
                modo = Constantes.Pedidos.ModosServicio.TODO_JUNTO;
                motivo = ModosServicioPermitidos.MOTIVO_NADA_CON_STOCK;
            }
            else if (sinFecha > 0)
            {
                modo = Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ;
                motivo = $"{sinFecha} línea{Plural(sinFecha)} sin stock y sin fecha de llegada: sale lo que hay y el resto en una sola entrega más.";
            }
            else
            {
                modo = Constantes.Pedidos.ModosServicio.TODO_JUNTO;
                DateTime? fecha = llegan.Where(l => l.FechaPrevista.HasValue).Select(l => l.FechaPrevista).Max();
                motivo = $"{llegan.Count} línea{Plural(llegan.Count)} llega{(llegan.Count == 1 ? string.Empty : "n")} del proveedor en días" +
                         (fecha.HasValue ? $" (prevista el {fecha.Value.ToString("dd/MM", CultureInfo.InvariantCulture)})" : string.Empty) +
                         ": sale todo junto cuando llegue.";
            }

            var sugerencia = new SugerenciaPorCausas
            {
                Modo = modo,
                Nombre = Constantes.Pedidos.ModosServicio.Nombre(modo),
                Motivo = motivo,
                Modos = ModosCentral(falta, conTiendas > 0, hayAhora)
            };
            sugerencia.ModosPermitidos = ModosServicioPermitidos.Permitidos(sugerencia.Modos);
            return sugerencia;
        }

        private static List<ModoServicioPermitidoDTO> ModosCentral(bool falta, bool hayTiendas, bool hayAhora)
        {
            var modos = new[]
            {
                Constantes.Pedidos.ModosServicio.TODO_JUNTO,
                Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO,
                Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS,
                Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ
            };
            return modos.Select(m =>
            {
                string motivo = null;
                if (m != Constantes.Pedidos.ModosServicio.TODO_JUNTO)
                {
                    if (!falta)
                    {
                        motivo = ModosServicioPermitidos.MOTIVO_TODO_VERDE;
                    }
                    else if (m == Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS && !hayTiendas)
                    {
                        motivo = ModosServicioPermitidos.MOTIVO_SIN_ROSAS;
                    }
                    else if (!hayTiendas && !hayAhora)
                    {
                        motivo = ModosServicioPermitidos.MOTIVO_NADA_CON_STOCK;
                    }
                }
                return new ModoServicioPermitidoDTO
                {
                    Modo = m,
                    Nombre = Constantes.Pedidos.ModosServicio.Nombre(m),
                    Motivo = motivo,
                    Permitido = motivo == null
                };
            }).ToList();
        }

        /// <summary>Tienda, almacén de otro tipo o sin productos: exactamente la regla de siempre (no depende del stock).</summary>
        private static SugerenciaPorCausas ConModosSinStock(byte modo, string motivo, ModosServicioPermitidos.TipoAlmacen tipo, bool hayLineasProducto)
        {
            var sugerencia = new SugerenciaPorCausas
            {
                Modo = modo,
                Nombre = Constantes.Pedidos.ModosServicio.Nombre(modo),
                Motivo = motivo,
                Modos = ModosServicioPermitidos.Calcular(tipo, 0, 0, 0, hayLineasProducto)
            };
            sugerencia.ModosPermitidos = ModosServicioPermitidos.Permitidos(sugerencia.Modos);
            return sugerencia;
        }

        private static string Plural(int n) => n == 1 ? string.Empty : "s";
    }
}
