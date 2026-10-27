using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Hosting;

namespace NestoAPI.Infraestructure.PedidosVenta.Anomalias
{
    /// <summary>
    /// NestoAPI#715 (corte 1): al grabar un pedido (POST y PUT de api/PedidosVenta), DESPUÉS de validar y guardar, evalúa las
    /// reglas de <see cref="SenalesAnomaliaPedido"/> y deja en PedidosAnomalias la fila de contexto del pedido (#716 2.5, 2.6) y
    /// una fila por señal (y por línea sin stock, #716 2.10). Garantías:
    /// <list type="bullet">
    /// <item>Apagado: no consulta nada ni lanza nada en segundo plano.</item>
    /// <item>Sombra: registra, pero el cliente no recibe nada (ni aviso ni error). Activo se comporta igual hasta el corte 4.</item>
    /// <item>Best-effort: nunca lanza ni retrasa la respuesta. La lectura del histórico (una conexión propia, ~1.600 lecturas
    /// lógicas en un pedido de 55 líneas) y la escritura van en segundo plano (QueueBackgroundWorkItem), como máximo
    /// <see cref="MAXIMO_EN_CURSO"/> a la vez; si hay más, la de ese pedido se pierde.</item>
    /// <item>Sin la tabla (Scripts/Issue715_PedidosAnomalias.sql) o con un fallo, un aviso a ELMAH por proceso y el pedido queda
    /// grabado igual.</item>
    /// </list>
    /// </summary>
    public static class RegistroAnomaliasPedido
    {
        public const int MESES_HISTORICO = 24;
        public const int MAXIMO_EN_CURSO = 2;

        // Sustituibles en los tests.
        internal static Func<IRepositorioAnomaliasPedido> CrearRepositorio { get; set; } = () => RepositorioAnomaliasPedidoSql.DesdeConfiguracion();
        internal static Action<Action> Lanzador { get; set; } = LanzarEnSegundoPlano;
        internal static Action<Exception> Registrador { get; set; } = ex => ElmahHelper.Log(ex, "NestoAPI#715");
        internal static Func<DateTime> Ahora { get; set; } = () => DateTime.Now;

        private static readonly SemaphoreSlim enCurso = new SemaphoreSlim(MAXIMO_EN_CURSO, MAXIMO_EN_CURSO);
        private static int avisadoSinTabla;
        private static int avisadoFallo;

        /// <summary>Para los tests: vuelve a avisar a ELMAH la próxima vez.</summary>
        internal static void ReiniciarAvisos()
        {
            avisadoSinTabla = 0;
            avisadoFallo = 0;
        }

        /// <summary>Las líneas grabadas antes de un PUT, reducidas (se toma ANTES de tocar las entidades).</summary>
        public static List<LineaGrabadaAnomalia> Instantanea(IEnumerable<LinPedidoVta> lineas)
        {
            try
            {
                return (lineas ?? Enumerable.Empty<LinPedidoVta>()).Select(LineaGrabadaAnomalia.Desde).Where(l => l != null).ToList();
            }
            catch
            {
                return new List<LineaGrabadaAnomalia>();
            }
        }

        /// <summary>
        /// Lanza la evaluación en segundo plano. Nunca lanza excepciones ni espera a nada.
        /// </summary>
        /// <param name="operacion"><see cref="OperacionesAnomalia"/>.</param>
        /// <param name="lineasAntes">Las líneas grabadas antes (PUT) o null (POST).</param>
        /// <param name="validacion">El resultado de GestorPrecios.EsPedidoValido, o null si no se validó.</param>
        /// <param name="cabeceraAplicacion">La cabecera X-Aplicacion, si vino (<see cref="AplicacionesOrigen.DesdeCabecera"/>).</param>
        /// <param name="nombrePrincipal">User.Identity.Name, para inferir la aplicación.</param>
        public static void Registrar(string operacion, PedidoVentaDTO pedido, IList<LineaGrabadaAnomalia> lineasAntes, RespuestaValidacion validacion,
            string cabeceraAplicacion, string nombrePrincipal)
        {
            try
            {
                if (pedido == null || InterruptorAnomalias.Modo == ModoAnomalias.Apagado)
                {
                    return;
                }
                string aplicacion = AplicacionesOrigen.Inferir(cabeceraAplicacion, pedido.Usuario, nombrePrincipal);
                InstantaneaAnomalias instantanea = InstantaneaAnomalias.Desde(operacion, pedido, lineasAntes, validacion, aplicacion, Ahora());
                Lanzador(() => Procesar(instantanea));
            }
            catch (Exception ex)
            {
                Avisar(ref avisadoFallo, new Exception($"[Anomalías #715] No se ha podido preparar el registro del pedido {pedido?.numero}. " +
                    $"El pedido está grabado igual. {ex.Message}", ex));
            }
        }

        /// <summary>El trabajo (en segundo plano). Devuelve las filas guardadas (para los tests); nunca lanza.</summary>
        internal static List<FilaAnomaliaPedido> Procesar(InstantaneaAnomalias instantanea)
        {
            var guardadas = new List<FilaAnomaliaPedido>();
            try
            {
                if (instantanea == null || string.IsNullOrEmpty(instantanea.Cliente))
                {
                    return guardadas;
                }
                if (!enCurso.Wait(0))
                {
                    return guardadas; // Ya hay bastantes en marcha: esta se pierde, es solo registro.
                }
                try
                {
                    IRepositorioAnomaliasPedido repositorio = CrearRepositorio();
                    if (repositorio == null || !repositorio.TablaExiste())
                    {
                        Avisar(ref avisadoSinTabla, new Exception("[Anomalías #715] No existe la tabla PedidosAnomalias (falta lanzar " +
                            "Scripts/Issue715_PedidosAnomalias.sql). Los pedidos se graban, pero no se registran las señales ni el contexto."));
                        return guardadas;
                    }
                    List<string> productos = instantanea.Lineas.Where(l => !l.Excluida).Select(l => l.Producto).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if (instantanea.Lineas.Any(l => !l.Excluida && l.Linea == 0))
                    {
                        // POST (y líneas nuevas del PUT): el DTO no trae el Nº de orden que acaba de dar la BD. Se lee del pedido ya grabado
                        // (consulta por clave) para que el etiquetado pueda seguir la línea.
                        try
                        {
                            ResolverLineasNuevas(instantanea, repositorio.LeerLineasActuales(instantanea.Empresa, new[] { instantanea.Pedido }));
                        }
                        catch (Exception ex)
                        {
                            Avisar(ref avisadoFallo, new Exception($"[Anomalías #715] No se han podido leer las líneas grabadas del pedido {instantanea.Pedido}; " +
                                $"las señales de las líneas nuevas irán sin Nº de orden. {ex.Message}", ex));
                        }
                    }
                    DateTime desde = instantanea.Fecha.AddMonths(-MESES_HISTORICO);
                    HistoricoCliente historico = repositorio.LeerHistorico(instantanea.Empresa, instantanea.Cliente, instantanea.Pedido, productos, desde);
                    List<KeyValuePair<string, string>> productosAlmacen = instantanea.Lineas.Where(l => !l.Excluida)
                        .Select(l => new KeyValuePair<string, string>(l.Producto, l.Almacen)).ToList();
                    ContextoGrabacion contexto;
                    try
                    {
                        contexto = repositorio.LeerContexto(instantanea.Empresa, instantanea.Cliente, instantanea.Contacto, productosAlmacen, instantanea.Fecha);
                    }
                    catch (Exception ex)
                    {
                        // El contexto es accesorio: sin él se registran las señales con las columnas de contexto a NULL.
                        contexto = new ContextoGrabacion();
                        Avisar(ref avisadoFallo, new Exception($"[Anomalías #715] No se ha podido leer el contexto del cliente {instantanea.Cliente} " +
                            $"(pedido {instantanea.Pedido}). Se registra sin deuda ni stock. {ex.Message}", ex));
                    }
                    List<SenalAnomalia> senales = SenalesAnomaliaPedido.Evaluar(instantanea, historico);
                    senales.AddRange(SenalesAnomaliaPedido.EvaluarStock(instantanea, contexto));
                    List<FilaAnomaliaPedido> filas = ConstruirFilas(instantanea, senales, contexto);
                    repositorio.Insertar(filas);
                    guardadas.AddRange(filas);
                }
                finally
                {
                    _ = enCurso.Release();
                }
            }
            catch (Exception ex)
            {
                Avisar(ref avisadoFallo, new Exception($"[Anomalías #715] No se han podido registrar las señales del pedido {instantanea?.Pedido}. " +
                    $"El pedido está grabado igual. {ex.Message}", ex));
            }
            return guardadas;
        }

        /// <summary>
        /// Da Nº de orden a las líneas nuevas (id 0) casándolas con las líneas grabadas por producto, cantidad y precio (y, si hay
        /// una sola candidata del producto, por producto). Las que no casan se quedan a 0 (irán sin Nº de orden). Puro.
        /// </summary>
        internal static void ResolverLineasNuevas(InstantaneaAnomalias instantanea, IList<EstadoLineaActual> grabadas)
        {
            if (instantanea == null || grabadas == null || grabadas.Count == 0)
            {
                return;
            }
            var usadas = new HashSet<int>(instantanea.Lineas.Where(l => l.Linea != 0).Select(l => l.Linea));
            foreach (LineaInstantaneaAnomalia linea in instantanea.Lineas.Where(l => !l.Excluida && l.Linea == 0))
            {
                List<EstadoLineaActual> candidatas = grabadas
                    .Where(g => g.Pedido == instantanea.Pedido && !usadas.Contains(g.Linea) && string.Equals(g.Producto?.Trim(), linea.Producto, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                EstadoLineaActual elegida = candidatas.FirstOrDefault(g => g.Cantidad == linea.Cantidad && Math.Abs(g.Precio - linea.Precio) < 0.005m)
                    ?? (candidatas.Count == 1 ? candidatas[0] : null);
                if (elegida != null)
                {
                    linea.Linea = elegida.Linea;
                    _ = usadas.Add(elegida.Linea);
                }
            }
        }

        /// <summary>La fila de contexto del pedido más una por señal. Puro.</summary>
        internal static List<FilaAnomaliaPedido> ConstruirFilas(InstantaneaAnomalias instantanea, IEnumerable<SenalAnomalia> senales, ContextoGrabacion contexto)
        {
            contexto = contexto ?? new ContextoGrabacion();
            Dictionary<int, LineaInstantaneaAnomalia> lineas = instantanea.Lineas.Where(l => l.Linea != 0).GroupBy(l => l.Linea).ToDictionary(g => g.Key, g => g.First());

            FilaAnomaliaPedido Base(string regla) => new FilaAnomaliaPedido
            {
                Fecha = instantanea.Fecha,
                Empresa = instantanea.Empresa,
                Pedido = instantanea.Pedido,
                Cliente = instantanea.Cliente,
                Contacto = instantanea.Contacto,
                Regla = regla,
                ImportePedido = instantanea.ImporteBruto,
                NumeroLineas = instantanea.NumeroLineas,
                Usuario = instantanea.Usuario,
                Aplicacion = instantanea.Aplicacion,
                Vendedor = instantanea.Vendedor,
                Operacion = instantanea.Operacion,
                Mostrado = false,
                VersionModelo = ReglasAnomalia.VERSION_REGLAS,
                DeudaTotal = contexto.DeudaTotal,
                DeudaVencida = contexto.DeudaVencida,
                DiasVencidoMasAntiguo = contexto.DiasVencidoMasAntiguo,
                FormaPagoFicha = contexto.FormaPagoFicha,
                FormaPagoPedido = instantanea.FormaPago
            };

            var filas = new List<FilaAnomaliaPedido> { Base(ReglasAnomalia.CONTEXTO) };
            foreach (SenalAnomalia senal in senales ?? Enumerable.Empty<SenalAnomalia>())
            {
                FilaAnomaliaPedido fila = Base(senal.Regla);
                fila.Valor = senal.Valor;
                fila.Esperado = senal.Esperado;
                fila.Puntuacion = senal.Puntuacion;
                fila.Producto = senal.Producto;
                if (senal.Linea.HasValue)
                {
                    // Línea nueva (id 0): aún no tiene Nº de orden; se guarda NULL y el etiquetado la busca por producto.
                    fila.Linea = senal.Linea.Value == 0 ? (int?)null : senal.Linea.Value;
                    LineaInstantaneaAnomalia linea = senal.Linea.Value != 0 && lineas.TryGetValue(senal.Linea.Value, out LineaInstantaneaAnomalia l)
                        ? l
                        : instantanea.Lineas.FirstOrDefault(x => x.Linea == senal.Linea.Value && string.Equals(x.Producto, senal.Producto, StringComparison.OrdinalIgnoreCase));
                    if (linea != null)
                    {
                        fila.CantidadGrabada = linea.Cantidad;
                        fila.PrecioGrabado = linea.Precio;
                        fila.DescuentoGrabado = linea.Descuento;
                        fila.StockAlGrabar = contexto.StockDe(linea.Producto, linea.Almacen);
                    }
                }
                filas.Add(fila);
            }
            return filas;
        }

        private static void Avisar(ref int bandera, Exception ex)
        {
            try
            {
                if (Interlocked.Exchange(ref bandera, 1) == 0)
                {
                    Registrador(ex);
                }
            }
            catch
            {
                // Ni el aviso puede romper nada
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
    }
}
