using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Hosting;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#563: todo el stock que necesitan los dos sugeridores, leído de una vez por la sombra.</summary>
    public class DatosSombraModoServicio
    {
        /// <summary>Stock, pendientes (sin el propio pedido) y reposiciones, como los lee <see cref="GestorStocks"/>.</summary>
        public ResumenStocksProductos Resumen { get; } = new ResumenStocksProductos();
        /// <summary>Productos.Estado (clave producto).</summary>
        public Dictionary<string, short> Estados { get; } = new Dictionary<string, short>();
        /// <summary>Pendiente de recibir de pedidos a proveedor enviados (clave producto|almacén).</summary>
        public Dictionary<string, int> PendienteRecibir { get; } = new Dictionary<string, int>();
        /// <summary>Fecha de recepción más temprana de esos pedidos (clave producto|almacén).</summary>
        public Dictionary<string, DateTime> FechaPrevista { get; } = new Dictionary<string, DateTime>();
    }

    /// <summary>
    /// NestoAPI#563: <see cref="IGestorStocks"/> en memoria sobre <see cref="DatosSombraModoServicio"/>, para que el
    /// sugeridor de colores de la sombra use EXACTAMENTE los mismos datos que el de causas (y sin ir a la BD).
    /// </summary>
    public class GestorStocksSombra : IGestorStocks
    {
        private readonly ResumenStocksProductos _resumen;

        public GestorStocksSombra(ResumenStocksProductos resumen)
        {
            _resumen = resumen ?? throw new ArgumentNullException(nameof(resumen));
        }

        public string ColorStock(string producto, string almacen, int cantidad) => GestorStocksPrecargado.ColorDesdeResumen(_resumen, producto, almacen, cantidad);
        public string ColorStock(string producto, string almacen) => ColorStock(producto, almacen, 0);
        public int Stock(string producto, string almacen) => ResumenStocksProductos.Valor(_resumen.StockAlmacen, ResumenStocksProductos.Clave(producto, almacen));
        public int UnidadesPendientesEntregarAlmacen(string producto, string almacen) => ResumenStocksProductos.Valor(_resumen.PendienteEntregarAlmacen, ResumenStocksProductos.Clave(producto, almacen));
        public int UnidadesDisponiblesTodosLosAlmacenes(string producto) => GestorStocksPrecargado.DisponibleTodos(_resumen, producto);
        public int UnidadesPendientesEntregar(string producto) => ResumenStocksProductos.Valor(_resumen.PendienteEntregarTotal, ResumenStocksProductos.Clave(producto));
        public int Stock(string producto)
            => ResumenStocksProductos.Valor(_resumen.StockSedes, ResumenStocksProductos.Clave(producto))
             + ResumenStocksProductos.Valor(_resumen.PendienteReposicion, ResumenStocksProductos.Clave(producto));
        public bool HayStockDisponibleDeTodo(PedidoVentaDTO pedido) => throw new NotSupportedException("Solo para la sombra del modo de servicio (#563).");
        public bool HayStockDisponibleDeTodo(PedidoVentaDTO pedido, string almacen) => throw new NotSupportedException("Solo para la sombra del modo de servicio (#563).");
    }

    /// <summary>NestoAPI#563: <see cref="IFuenteDatosCausasModoServicio"/> sobre los datos de la sombra.</summary>
    public class FuenteDatosCausasSombra : IFuenteDatosCausasModoServicio
    {
        private readonly DatosSombraModoServicio _datos;

        public FuenteDatosCausasSombra(DatosSombraModoServicio datos)
        {
            _datos = datos ?? throw new ArgumentNullException(nameof(datos));
        }

        public DatosStockCausaModoServicio Leer(string producto, string almacen)
        {
            string claveProducto = ResumenStocksProductos.Clave(producto);
            string claveAlmacen = ResumenStocksProductos.Clave(producto, almacen);
            return new DatosStockCausaModoServicio
            {
                StockAlmacen = ResumenStocksProductos.Valor(_datos.Resumen.StockAlmacen, claveAlmacen),
                PendientesAlmacen = ResumenStocksProductos.Valor(_datos.Resumen.PendienteEntregarAlmacen, claveAlmacen),
                DisponibleTodos = GestorStocksPrecargado.DisponibleTodos(_datos.Resumen, producto),
                EstadoProducto = _datos.Estados.TryGetValue(claveProducto, out short estado) ? estado : (short?)null,
                PendienteRecibir = ResumenStocksProductos.Valor(_datos.PendienteRecibir, claveAlmacen),
                FechaPrevista = _datos.FechaPrevista.TryGetValue(claveAlmacen, out DateTime fecha) ? fecha : (DateTime?)null
            };
        }
    }

    /// <summary>NestoAPI#563: lo mínimo del pedido que necesita la sombra, copiado en el hilo de la petición.</summary>
    public class InstantaneaSombraModoServicio
    {
        public string Origen { get; set; }
        public string Empresa { get; set; }
        public int? Pedido { get; set; }
        public bool EsPresupuesto { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Usuario { get; set; }
        /// <summary>El modo que vio el usuario (plantilla) o con el que se grabó el pedido (crear).</summary>
        public byte? ModoPedido { get; set; }
        public PedidoVentaDTO PedidoReducido { get; set; }

        public static InstantaneaSombraModoServicio Desde(string origen, PedidoVentaDTO pedido, byte? modoPedido)
        {
            return new InstantaneaSombraModoServicio
            {
                Origen = origen,
                Empresa = pedido?.empresa?.Trim(),
                Pedido = pedido != null && pedido.numero > 0 ? pedido.numero : (int?)null,
                EsPresupuesto = pedido?.EsPresupuesto ?? false,
                Cliente = pedido?.cliente?.Trim(),
                Contacto = pedido?.contacto?.Trim(),
                Usuario = pedido?.Usuario?.Trim(),
                ModoPedido = modoPedido,
                PedidoReducido = new PedidoVentaDTO
                {
                    empresa = pedido?.empresa,
                    numero = pedido?.numero ?? 0,
                    Lineas = ClasificadorCausasModoServicio.LineasProducto(pedido)
                        .Select(l => new LineaPedidoVentaDTO
                        {
                            Producto = l.Producto,
                            almacen = l.almacen,
                            Cantidad = l.Cantidad,
                            tipoLinea = l.tipoLinea
                        })
                        .ToList()
                }
            };
        }
    }

    /// <summary>NestoAPI#563: una fila de <c>ModoServicioSombra</c>.</summary>
    public class FilaSombraModoServicio
    {
        public string Origen { get; set; }
        public string Empresa { get; set; }
        public int? Pedido { get; set; }
        public bool EsPresupuesto { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Usuario { get; set; }
        public byte? ModoPedido { get; set; }
        public byte ModoColores { get; set; }
        public string PermitidosColores { get; set; }
        public string MotivoColores { get; set; }
        public byte ModoCausas { get; set; }
        public string PermitidosCausas { get; set; }
        public string MotivoCausas { get; set; }
        public bool MismoModo { get; set; }
        public bool MismosPermitidos { get; set; }
        public bool Coincide => MismoModo && MismosPermitidos;
        /// <summary>Una línea por producto+almacén con su reparto de causas y su color.</summary>
        public string Causas { get; set; }
    }

    /// <summary>NestoAPI#563: lectura de los datos y escritura de la tabla de la sombra.</summary>
    public interface IRepositorioSombraModoServicio
    {
        /// <summary>Si ya se ha creado la tabla (Scripts/Issue563_SombraModoServicio.sql).</summary>
        bool ExisteTabla();
        /// <summary>El stock de los productos, con los pendientes SIN las líneas del pedido <paramref name="pedidoExcluir"/>.</summary>
        DatosSombraModoServicio LeerDatos(string empresa, int? pedidoExcluir, IEnumerable<string> productos);
        void Guardar(FilaSombraModoServicio fila);
    }

    public enum ModoSombraModoServicio
    {
        Apagado,
        /// <summary>Registra todas las comparaciones.</summary>
        Todo,
        /// <summary>Registra solo las que no coinciden.</summary>
        Diferencias
    }

    /// <summary>
    /// NestoAPI#563: SOMBRA del sugeridor de modo de servicio por causas. Cada vez que se calcula la sugerencia de
    /// siempre (plantilla, <c>POST api/PedidosVenta/ModoServicioSugerido</c>) o se crea un pedido/presupuesto, se
    /// calculan en SEGUNDO PLANO las dos (colores y causas) con los mismos datos y se guarda la comparación en
    /// <c>ModoServicioSombra</c>. Garantías:
    /// <list type="bullet">
    /// <item>No cambia nada de lo que ve el usuario: solo copia el pedido (líneas de producto) y vuelve.</item>
    /// <item>No ralentiza: el trabajo (interruptor incluido) va en otro hilo; si ya hay
    /// <see cref="MAXIMO_EN_CURSO"/> en marcha, se descarta.</item>
    /// <item>No rompe: cualquier fallo se traga y se deja en ELMAH como mucho una vez cada
    /// <see cref="INTERVALO_ELMAH"/>.</item>
    /// <item>Interruptor <see cref="Constantes.ParametrosUsuario.MODO_SERVICIO_SOMBRA"/> bajo «(defecto)», APAGADO sin
    /// fila; si la lectura falla, apagado. Encendido y sin tabla, no lee nada (se comprueba cada 10 minutos).</item>
    /// </list>
    /// Al crear, el pedido ya está grabado: sus líneas se excluyen de los pendientes para ver el stock de antes.
    /// </summary>
    public static class SombraModoServicio
    {
        public const string ORIGEN_PLANTILLA = "Plantilla";
        public const string ORIGEN_CREAR = "Crear";
        public const int MAXIMO_EN_CURSO = 2;
        public const int MAXIMO_PRODUCTOS = 200;
        public static readonly TimeSpan DURACION_INTERRUPTOR = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan DURACION_EXISTE_TABLA = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan INTERVALO_ELMAH = TimeSpan.FromMinutes(30);

        // Sustituibles en los tests.
        internal static Func<string> LeerInterruptor { get; set; } = () => new LectorParametrosUsuario().LeerParametro(
            Constantes.Empresas.EMPRESA_POR_DEFECTO, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO, Constantes.ParametrosUsuario.MODO_SERVICIO_SOMBRA);
        internal static Func<IRepositorioSombraModoServicio> CrearRepositorio { get; set; } = () => RepositorioSombraModoServicioSql.DesdeConfiguracion();
        internal static Action<Action> Lanzador { get; set; } = LanzarEnSegundoPlano;
        internal static Action<Exception> Registrador { get; set; } = ex => ElmahHelper.Log(ex, "NestoAPI#563");
        internal static Func<DateTime> Ahora { get; set; } = () => DateTime.UtcNow;

        private static readonly object cerrojo = new object();
        private static readonly SemaphoreSlim enCurso = new SemaphoreSlim(MAXIMO_EN_CURSO, MAXIMO_EN_CURSO);
        private static ModoSombraModoServicio modoLeido;
        private static DateTime modoLeidoUtc = DateTime.MinValue;
        private static bool? existeTabla;
        private static DateTime existeTablaUtc = DateTime.MinValue;
        private static DateTime ultimoElmahUtc = DateTime.MinValue;
        private static int fallosSinAvisar;

        /// <summary>
        /// Lanza la comparación en segundo plano. Nunca lanza excepciones ni espera a nada.
        /// </summary>
        /// <param name="modoPedido">El modo que vio el usuario (plantilla) o con el que se ha grabado (crear).</param>
        public static void Registrar(string origen, PedidoVentaDTO pedido, byte? modoPedido)
        {
            try
            {
                if (pedido == null)
                {
                    return;
                }
                InstantaneaSombraModoServicio instantanea = InstantaneaSombraModoServicio.Desde(origen, pedido, modoPedido);
                Lanzador(() => Procesar(instantanea));
            }
            catch (Exception ex)
            {
                Avisar(ex);
            }
        }

        /// <summary>El trabajo de la sombra (en segundo plano). Tampoco lanza nunca.</summary>
        internal static void Procesar(InstantaneaSombraModoServicio instantanea)
        {
            try
            {
                ModoSombraModoServicio modo = Modo();
                if (modo == ModoSombraModoServicio.Apagado || instantanea?.PedidoReducido == null)
                {
                    return;
                }
                List<string> productos = instantanea.PedidoReducido.Lineas.Select(l => l.Producto.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (!productos.Any() || productos.Count > MAXIMO_PRODUCTOS)
                {
                    return;
                }
                if (!enCurso.Wait(0))
                {
                    return; // Ya hay bastantes en marcha: esta se pierde, es solo diagnóstico.
                }
                try
                {
                    IRepositorioSombraModoServicio repositorio = CrearRepositorio();
                    if (!TablaExiste(repositorio))
                    {
                        return;
                    }
                    DatosSombraModoServicio datos = repositorio.LeerDatos(instantanea.Empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO,
                        instantanea.Pedido, productos);
                    FilaSombraModoServicio fila = Comparar(instantanea, datos);
                    if (modo == ModoSombraModoServicio.Diferencias && fila.Coincide)
                    {
                        return;
                    }
                    repositorio.Guardar(fila);
                }
                finally
                {
                    _ = enCurso.Release();
                }
            }
            catch (Exception ex)
            {
                Avisar(ex);
            }
        }

        /// <summary>Las dos sugerencias con los mismos datos y su comparación. Puro.</summary>
        internal static FilaSombraModoServicio Comparar(InstantaneaSombraModoServicio instantanea, DatosSombraModoServicio datos)
        {
            SugeridorModoServicio.Sugerencia colores = SugeridorModoServicio.Sugerir(instantanea.PedidoReducido, new GestorStocksSombra(datos.Resumen));
            SugerenciaPorCausas causas = ClasificadorCausasModoServicio.Sugerir(instantanea.PedidoReducido, new FuenteDatosCausasSombra(datos));
            string permitidosColores = Lista(colores.ModosPermitidos);
            string permitidosCausas = Lista(causas.ModosPermitidos);
            return new FilaSombraModoServicio
            {
                Origen = instantanea.Origen,
                Empresa = instantanea.Empresa,
                Pedido = instantanea.Pedido,
                EsPresupuesto = instantanea.EsPresupuesto,
                Cliente = instantanea.Cliente,
                Contacto = instantanea.Contacto,
                Usuario = instantanea.Usuario,
                ModoPedido = instantanea.ModoPedido,
                ModoColores = colores.Modo,
                PermitidosColores = permitidosColores,
                MotivoColores = colores.Motivo,
                ModoCausas = causas.Modo,
                PermitidosCausas = permitidosCausas,
                MotivoCausas = causas.Motivo,
                MismoModo = colores.Modo == causas.Modo,
                MismosPermitidos = permitidosColores == permitidosCausas,
                Causas = string.Join(Environment.NewLine, causas.Lineas.Select(l =>
                    l.Resumen() + " [" + GestorStocksPrecargado.ColorDesdeResumen(datos.Resumen, l.Producto, l.Almacen, l.Cantidad) + "]"))
            };
        }

        internal static ModoSombraModoServicio InterpretarModo(string valor)
        {
            string limpio = valor?.Trim();
            if (limpio == "1" || string.Equals(limpio, "Todo", StringComparison.OrdinalIgnoreCase))
            {
                return ModoSombraModoServicio.Todo;
            }
            return string.Equals(limpio, "Diferencias", StringComparison.OrdinalIgnoreCase)
                ? ModoSombraModoServicio.Diferencias
                : ModoSombraModoServicio.Apagado;
        }

        /// <summary>Para los tests: olvida lo leído (interruptor, tabla, avisos).</summary>
        internal static void Reiniciar()
        {
            lock (cerrojo)
            {
                modoLeido = ModoSombraModoServicio.Apagado;
                modoLeidoUtc = DateTime.MinValue;
                existeTabla = null;
                existeTablaUtc = DateTime.MinValue;
                ultimoElmahUtc = DateTime.MinValue;
                fallosSinAvisar = 0;
            }
        }

        private static ModoSombraModoServicio Modo()
        {
            lock (cerrojo)
            {
                DateTime ahora = Ahora();
                if (modoLeidoUtc != DateTime.MinValue && ahora - modoLeidoUtc < DURACION_INTERRUPTOR)
                {
                    return modoLeido;
                }
                ModoSombraModoServicio leido;
                try
                {
                    leido = InterpretarModo(LeerInterruptor());
                }
                catch
                {
                    leido = ModoSombraModoServicio.Apagado; // sin poder leerlo, apagado
                }
                modoLeido = leido;
                modoLeidoUtc = ahora;
                return leido;
            }
        }

        private static bool TablaExiste(IRepositorioSombraModoServicio repositorio)
        {
            lock (cerrojo)
            {
                DateTime ahora = Ahora();
                if (existeTabla.HasValue && ahora - existeTablaUtc < DURACION_EXISTE_TABLA)
                {
                    return existeTabla.Value;
                }
            }
            bool existe;
            try
            {
                existe = repositorio.ExisteTabla();
            }
            catch (Exception ex)
            {
                Avisar(ex);
                existe = false;
            }
            lock (cerrojo)
            {
                existeTabla = existe;
                existeTablaUtc = Ahora();
            }
            if (!existe)
            {
                Avisar(new InvalidOperationException("No existe la tabla ModoServicioSombra: falta ejecutar Scripts/Issue563_SombraModoServicio.sql"));
            }
            return existe;
        }

        private static void Avisar(Exception ex)
        {
            try
            {
                int acumulados;
                lock (cerrojo)
                {
                    DateTime ahora = Ahora();
                    if (ultimoElmahUtc != DateTime.MinValue && ahora - ultimoElmahUtc < INTERVALO_ELMAH)
                    {
                        fallosSinAvisar++;
                        return;
                    }
                    ultimoElmahUtc = ahora;
                    acumulados = fallosSinAvisar;
                    fallosSinAvisar = 0;
                }
                Registrador(new Exception($"[Sombra modo de servicio #563] {ex?.Message}" +
                    (acumulados > 0 ? $" (y {acumulados} fallo{(acumulados == 1 ? string.Empty : "s")} más sin avisar desde el último aviso)" : string.Empty) +
                    ". No afecta a los pedidos.", ex));
            }
            catch
            {
                // Ni el aviso puede romper nada.
            }
        }

        private static void LanzarEnSegundoPlano(Action trabajo)
        {
            if (HostingEnvironment.IsHosted)
            {
                HostingEnvironment.QueueBackgroundWorkItem(_ => trabajo());
            }
            else
            {
                _ = Task.Run(trabajo);
            }
        }

        private static string Lista(IEnumerable<byte> modos)
            => string.Join(",", (modos ?? Enumerable.Empty<byte>()).OrderBy(m => m));
    }
}
