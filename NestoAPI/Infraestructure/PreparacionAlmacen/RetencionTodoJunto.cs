using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>NestoAPI#858 punto 4: los estados de una retención por falta (PedidosRetenidosPorFalta.Estado).</summary>
    public static class EstadosRetencionPorFalta
    {
        public const string PENDIENTE = "Pendiente";
        public const string SERVIR_LO_QUE_HAY = "ServirLoQueHay";
        public const string ESPERAR = "Esperar";

        /// <summary>«servirloquehay» → «ServirLoQueHay»; null si no es una decisión.</summary>
        public static string Decision(string texto)
        {
            string limpio = texto?.Trim();
            if (string.Equals(limpio, SERVIR_LO_QUE_HAY, StringComparison.OrdinalIgnoreCase))
            {
                return SERVIR_LO_QUE_HAY;
            }
            return string.Equals(limpio, ESPERAR, StringComparison.OrdinalIgnoreCase) ? ESPERAR : null;
        }
    }

    /// <summary>Una línea con falta y lo que hace falta para saber si retiene el pedido.</summary>
    public class LineaTodoJuntoSalida
    {
        public int Linea { get; set; }
        public int Pedido { get; set; }
        public decimal BaseImponible { get; set; }
        public bool Ficticio { get; set; }
        public bool ServirJunto { get; set; }
        public byte? ModoServicio { get; set; }
    }

    public class RetencionPorFaltaNueva
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public int Picking { get; set; }
        public string Productos { get; set; }
        public DateTime Plazo { get; set; }
    }

    /// <summary>
    /// NestoAPI#858 punto 4: lo que la transacción de una salida (Terminar o «No está») sabe hacer para retener un pedido «todo junto».
    /// Es aparte de <see cref="ITransaccionSalida"/> a propósito: una transacción que no lo implementa (los dobles de los tests de
    /// antes) no retiene nada.
    /// </summary>
    public interface IRetencionTodoJuntoSalida
    {
        Task<List<LineaTodoJuntoSalida>> LeerLineasTodoJunto(string empresa, IReadOnlyCollection<int> lineas);

        /// <summary>
        /// Guarda la retención (si el pedido ya tiene una Pendiente, no crea otra) en la MISMA transacción. Devuelve su plazo, o null
        /// si no existe la tabla (script sin lanzar).
        /// </summary>
        Task<DateTime?> RetenerPorFalta(RetencionPorFaltaNueva retencion);
    }

    /// <summary>NestoAPI#858 punto 4: las reglas puras de la retención «todo junto» por falta.</summary>
    public static class ReglasRetencionTodoJunto
    {
        public const string CLAVE_INTERRUPTOR = "Picking.TodoJuntoRetener";
        public const string CLAVE_PLAZO = "Picking.TodoJuntoPlazoHoras";
        public const int PLAZO_HORAS_POR_DEFECTO = 2;
        /// <summary>Horario laboral de Algete para el plazo (no había uno común para plazos: L-V 8:00-18:00).</summary>
        public const int HORA_INICIO = 8;
        public const int HORA_FIN = 18;
        public const string TEXTO_PACKING = "Esperando al vendedor: no empaquetar";
        public const string TIPO_AVISO_VENCIDA = "PedidoRetenidoPorFaltaEsperar";

        private static readonly CultureInfo castellano = new CultureInfo("es-ES");

        /// <summary>
        /// Los pedidos a retener: los que tienen alguna falta que se queda pendiente (no muestra, no quitada) de una línea de pago
        /// (base imponible &gt; 0, no ficticia), en modo «todo junto» y de los que sale algo más (si no sale nada, ya espera entero).
        /// </summary>
        public static List<int> PedidosARetener(IEnumerable<FaltaPickingPedido> faltas, IEnumerable<LineaTodoJuntoSalida> lineas)
        {
            Dictionary<int, LineaTodoJuntoSalida> porLinea = (lineas ?? Enumerable.Empty<LineaTodoJuntoSalida>())
                .GroupBy(l => l.Linea).ToDictionary(g => g.Key, g => g.First());
            return (faltas ?? Enumerable.Empty<FaltaPickingPedido>())
                .Where(EsCandidata)
                .Where(f => porLinea.TryGetValue(f.Linea, out LineaTodoJuntoSalida l) && l.BaseImponible > 0 && !l.Ficticio
                    && Constantes.Pedidos.ModosServicio.Efectivo(l.ModoServicio, l.ServirJunto) == Constantes.Pedidos.ModosServicio.TODO_JUNTO)
                .Select(f => f.Pedido).Distinct().OrderBy(p => p).ToList();
        }

        public static bool EsCandidata(FaltaPickingPedido f) =>
            f != null && f.Unidades > 0 && !f.EsMuestra && !f.QuitadaDelPedido && !f.NoSaleNadaDelPedido;

        /// <summary>
        /// Suma horas laborables (L-V de <see cref="HORA_INICIO"/> a <see cref="HORA_FIN"/>, sin festivos de Algete). Fuera de
        /// horario, se empieza a contar en la siguiente apertura.
        /// </summary>
        public static DateTime SumarHorasLaborables(DateTime desde, double horas, Func<DateTime, bool> esFestivo)
        {
            esFestivo = esFestivo ?? (d => false);
            DateTime momento = desde;
            double quedan = Math.Max(0, horas);
            for (int vueltas = 0; vueltas < 400; vueltas++)
            {
                DateTime dia = momento.Date;
                bool laborable = dia.DayOfWeek != DayOfWeek.Saturday && dia.DayOfWeek != DayOfWeek.Sunday && !esFestivo(dia);
                DateTime apertura = dia.AddHours(HORA_INICIO);
                DateTime cierre = dia.AddHours(HORA_FIN);
                if (!laborable || momento >= cierre)
                {
                    momento = dia.AddDays(1).AddHours(HORA_INICIO);
                    continue;
                }
                if (momento < apertura)
                {
                    momento = apertura;
                }
                double hoy = (cierre - momento).TotalHours;
                if (quedan <= hoy)
                {
                    return momento.AddHours(quedan);
                }
                quedan -= hoy;
                momento = dia.AddDays(1).AddHours(HORA_INICIO);
            }
            return momento;
        }

        /// <summary>«17404 x2, 30111 x1» (cabe en 400).</summary>
        public static string Productos(IEnumerable<FaltaPickingPedido> faltas)
        {
            string texto = string.Join(", ", (faltas ?? Enumerable.Empty<FaltaPickingPedido>()).Where(EsCandidata)
                .GroupBy(f => f.Producto?.Trim()).Select(g => $"{g.Key} x{g.Sum(f => f.Unidades)}"));
            return texto.Length > 400 ? texto.Substring(0, 397) + "..." : texto;
        }

        public static string Plazo(DateTime plazo) => plazo.ToString("dddd d 'a las' HH:mm", castellano);

        /// <summary>Lo que se contesta a quien intenta empaquetar, albaranear o facturar un pedido retenido por falta.</summary>
        public static string MotivoBloqueo(int pedido, DateTime plazo) =>
            $"{TEXTO_PACKING}. El pedido {pedido} es «todo junto» y le falta un producto: está esperando a que el vendedor decida si se sirve " +
            $"lo que hay o se espera a tenerlo completo (hasta el {Plazo(plazo)}; si no contesta, se esperará).";

        /// <summary>Lo que añade el aviso de faltas a un pedido retenido.</summary>
        public static string TextoAviso(DateTime plazo) =>
            $" El pedido es «todo junto»: queda RETENIDO y no se empaqueta hasta que decida si se sirve lo que hay o se espera a tenerlo " +
            $"completo. Puede decidirlo hasta el {Plazo(plazo)}; si no contesta, se esperará a tenerlo completo.";

        /// <summary>Encendido salvo «0», «false», «no», «off» o «apagado» (como Salidas.NoEstaRegulariza).</summary>
        public static bool Interpretar(string valor) => ServicioNoEstaSalida.Interpretar(valor);

        public static int InterpretarPlazo(string valor) =>
            int.TryParse(valor?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int horas) && horas > 0 && horas <= 72
                ? horas : PLAZO_HORAS_POR_DEFECTO;

        // ---------------- Lo que se lee de fuera (sustituible en los tests) ----------------

        internal static Func<bool> Encendido = () =>
        {
            try
            {
                return Interpretar(NestoAPI.Controllers.ParametrosUsuarioController.LeerParametroSinDefecto(
                    Constantes.Empresas.EMPRESA_POR_DEFECTO, ServicioNoEstaSalida.USUARIO_DEFECTO, CLAVE_INTERRUPTOR));
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Todo junto #858] No se ha podido leer {CLAVE_INTERRUPTOR}: se deja encendido. {ex.Message}", ex));
                return true;
            }
        };

        internal static Func<int> PlazoHoras = () =>
        {
            try
            {
                return InterpretarPlazo(NestoAPI.Controllers.ParametrosUsuarioController.LeerParametroSinDefecto(
                    Constantes.Empresas.EMPRESA_POR_DEFECTO, ServicioNoEstaSalida.USUARIO_DEFECTO, CLAVE_PLAZO));
            }
            catch
            {
                return PLAZO_HORAS_POR_DEFECTO;
            }
        };

        internal static Func<DateTime> Ahora = () => DateTime.Now;
        internal static Func<DateTime, bool> EsFestivo = d => GestorFestivos.EsFestivo(d, Constantes.Almacenes.ALGETE);

        private static int sinTablaRegistrado;

        /// <summary>
        /// El paso de la retención dentro de la transacción de la salida, después de quitar las faltas. Sin <see cref="IRetencionTodoJuntoSalida"/>,
        /// apagado o sin tabla, no hace nada (todo como antes). Marca en <paramref name="avisar"/> los pedidos retenidos.
        /// </summary>
        internal static async Task RetenerSiHaceFalta(string empresa, int picking, ITransaccionSalida tx, List<FaltaPickingPedido> avisar)
        {
            if (!(tx is IRetencionTodoJuntoSalida retencion) || avisar == null || !avisar.Any(EsCandidata) || !Encendido())
            {
                return;
            }
            List<int> lineas = avisar.Where(EsCandidata).Select(f => f.Linea).Distinct().ToList();
            List<LineaTodoJuntoSalida> datos = await retencion.LeerLineasTodoJunto(empresa, lineas).ConfigureAwait(false) ?? new List<LineaTodoJuntoSalida>();
            List<int> pedidos = PedidosARetener(avisar, datos);
            if (!pedidos.Any())
            {
                return;
            }
            DateTime plazo = SumarHorasLaborables(Ahora(), PlazoHoras(), EsFestivo);
            foreach (int pedido in pedidos)
            {
                DateTime? guardado = await retencion.RetenerPorFalta(new RetencionPorFaltaNueva
                {
                    Empresa = empresa?.Trim(),
                    Pedido = pedido,
                    Picking = picking,
                    Productos = Productos(avisar.Where(f => f.Pedido == pedido)),
                    Plazo = plazo
                }).ConfigureAwait(false);
                if (!guardado.HasValue)
                {
                    if (System.Threading.Interlocked.Exchange(ref sinTablaRegistrado, 1) == 0)
                    {
                        ElmahHelper.Log(new Exception("[Todo junto #858] Falta lanzar Scripts/Issue858_PedidosRetenidosPorFalta.sql: los pedidos «todo junto» " +
                            "con falta salen como antes (sin retener)."));
                    }
                    return;
                }
                foreach (FaltaPickingPedido falta in avisar.Where(f => f.Pedido == pedido))
                {
                    falta.Retenido = true;
                    falta.PlazoRetencion = guardado.Value;
                }
            }
        }
    }

    /// <summary>
    /// NestoAPI#858: cuando una retención nace (Terminar o «No está»), se decide o se aplica por plazo, «haCambiado» al grupo del
    /// packing de su picking en el AlmacenHub (<see cref="AvisosAlmacen.GrupoPacking"/>, el mismo que usa el packing para refrescar
    /// las PDA). No hay grupo de «pendiente de ubicar» ni uno por pedido en Nesto: no se inventan. Después de guardar y nunca lanza.
    /// </summary>
    public static class AvisosRetencionPorFalta
    {
        /// <summary>El emisor: no es ninguna PDA, así que no se excluye a nadie del grupo.</summary>
        public const string DISPOSITIVO = "NestoAPI";

        public static bool HayRetenidos(IEnumerable<FaltaPickingPedido> faltas) => faltas?.Any(f => f != null && f.Retenido) == true;

        public static void Packing(IAvisosAlmacen avisos, int picking)
        {
            if (avisos == null || picking <= 0)
            {
                return;
            }
            try
            {
                avisos.HaCambiado(AvisosAlmacen.GrupoPacking(picking), null, null, DISPOSITIVO);
            }
            catch (Exception ex)
            {
                try
                {
                    ElmahHelper.Log(new Exception($"[Todo junto #858] No se ha podido avisar al packing del picking {picking}: {ex.Message}", ex));
                }
                catch
                {
                    // nada: el aviso es un extra
                }
            }
        }
    }

    // ---------------- Decisión del vendedor, plazo y bloqueos ----------------

    public class RetencionPorFaltaDTO
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public int Picking { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string NombreCliente { get; set; }
        public string Productos { get; set; }
        public DateTime Fecha { get; set; }
        public DateTime Plazo { get; set; }
        /// <summary>Pendiente, ServirLoQueHay o Esperar.</summary>
        public string Estado { get; set; }
        public string UsuarioDecision { get; set; }
        public DateTime? FechaDecision { get; set; }
        public bool PorPlazo { get; set; }
        /// <summary>Solo lectura: quien pregunta puede decidir.</summary>
        public bool PuedeDecidir { get; set; }
        public string Mensaje { get; set; }
    }

    public class DecidirRetencionPorFaltaDTO
    {
        /// <summary>«ServirLoQueHay» o «Esperar».</summary>
        public string Decision { get; set; }
    }

    public class ResultadoRetencionPorFalta<T>
    {
        public HttpStatusCode Codigo { get; set; } = HttpStatusCode.OK;
        public string Mensaje { get; set; }
        public T Contenido { get; set; }

        public static ResultadoRetencionPorFalta<T> Bien(T contenido) => new ResultadoRetencionPorFalta<T> { Contenido = contenido };
        public static ResultadoRetencionPorFalta<T> Mal(HttpStatusCode codigo, string mensaje) =>
            new ResultadoRetencionPorFalta<T> { Codigo = codigo, Mensaje = mensaje };
    }

    public class ResultadoEsperar
    {
        public bool Hecho { get; set; }
        /// <summary>No se ha hecho porque el picking todavía se está recogiendo (se intenta otra vez después).</summary>
        public bool PickingSinTerminar { get; set; }
        /// <summary>Ya no estaba Pendiente.</summary>
        public bool YaDecidida { get; set; }
        public int LineasSacadas { get; set; }
        public int UnidadesAPendienteDeUbicar { get; set; }
    }

    public interface IRepositorioRetencionesPorFalta
    {
        Task<bool> TablaDisponible();
        Task<RetencionPorFaltaDTO> LeerPendiente(string empresa, int pedido);
        Task<List<RetencionPorFaltaDTO>> LeerPendientes();
        Task<List<RetencionPorFaltaDTO>> LeerVencidas(DateTime ahora);
        /// <summary>ServirLoQueHay: solo cambia el estado si sigue Pendiente. False si ya no lo estaba.</summary>
        Task<bool> ServirLoQueHay(int id, string usuario);
        /// <summary>
        /// Esperar, en UNA transacción: cambia el estado (si sigue Pendiente) y, si el picking ya está terminado, saca del picking las
        /// líneas pendientes del pedido: su reserva pasa a «pendiente de ubicar» (lo cogido está en el carro) y Picking = 0, con rastro.
        /// </summary>
        Task<ResultadoEsperar> Esperar(RetencionPorFaltaDTO retencion, string usuario, bool porPlazo);
    }

    /// <summary>NestoAPI#858 punto 4: quién decide y el servicio de las decisiones (endpoints y job).</summary>
    public class ServicioRetencionesPorFalta
    {
        public static readonly IReadOnlyList<string> GRUPOS = new[]
        {
            Constantes.GruposSeguridad.ALMACEN, Constantes.GruposSeguridad.ADMINISTRACION,
            Constantes.GruposSeguridad.DIRECCION, Constantes.GruposSeguridad.INFORMATICA
        };

        public const string MENSAJE_SIN_SCRIPT = "Todavía no se pueden retener los pedidos «todo junto» con falta: falta lanzar el script " +
            "Scripts/Issue858_PedidosRetenidosPorFalta.sql. Avise a Informática.";

        private readonly IRepositorioRetencionesPorFalta repositorio;
        private readonly Func<string, int, ComercialesPedido> leerComerciales;
        private readonly Func<DateTime> ahora;
        /// <summary>«haCambiado» al packing del picking al decidir o aplicar por plazo. Null: no se avisa.</summary>
        private readonly IAvisosAlmacen avisos;

        public ServicioRetencionesPorFalta(IRepositorioRetencionesPorFalta repositorio, Func<string, int, ComercialesPedido> leerComerciales,
            Func<DateTime> ahora = null, IAvisosAlmacen avisos = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.leerComerciales = leerComerciales;
            this.ahora = ahora ?? (() => DateTime.Now);
            this.avisos = avisos;
        }

        public static bool EsDeLosGrupos(IPrincipal usuario) => usuario != null && GRUPOS.Any(g => usuario.IsInRoleSinDominio(g));

        /// <summary>Quien hizo el pedido, uno de sus vendedores (claim «Vendedor» o usuario del vendedor) o los grupos.</summary>
        public static bool PuedeDecidir(IPrincipal usuario, ComercialesPedido comerciales)
        {
            if (usuario?.Identity?.IsAuthenticated != true)
            {
                return false;
            }
            if (EsDeLosGrupos(usuario))
            {
                return true;
            }
            if (comerciales == null)
            {
                return false;
            }
            string yo = SinDominio(usuario.Identity.Name);
            if (yo != null && string.Equals(yo, SinDominio(comerciales.UsuarioPedido), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            string vendedor = (usuario.Identity as ClaimsIdentity)?.FindFirst("Vendedor")?.Value?.Trim();
            return (comerciales.Comerciales ?? new List<ComercialPedido>()).Any(c =>
                (!string.IsNullOrEmpty(vendedor) && string.Equals(c.Vendedor?.Trim(), vendedor, StringComparison.OrdinalIgnoreCase))
                || (yo != null && (c.Usuarios ?? new List<string>()).Any(u => string.Equals(SinDominio(u), yo, StringComparison.OrdinalIgnoreCase))));
        }

        internal static string SinDominio(string usuario)
        {
            string limpio = usuario?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 ? limpio.Substring(barra + 1) : limpio;
        }

        public async Task<ResultadoRetencionPorFalta<List<RetencionPorFaltaDTO>>> Leer(IPrincipal usuario)
        {
            if (!await repositorio.TablaDisponible().ConfigureAwait(false))
            {
                return ResultadoRetencionPorFalta<List<RetencionPorFaltaDTO>>.Mal(HttpStatusCode.ServiceUnavailable, MENSAJE_SIN_SCRIPT);
            }
            List<RetencionPorFaltaDTO> pendientes = await repositorio.LeerPendientes().ConfigureAwait(false) ?? new List<RetencionPorFaltaDTO>();
            bool todos = EsDeLosGrupos(usuario);
            var resultado = new List<RetencionPorFaltaDTO>();
            foreach (RetencionPorFaltaDTO r in pendientes)
            {
                r.PuedeDecidir = todos || PuedeDecidir(usuario, leerComerciales?.Invoke(r.Empresa, r.Pedido));
                if (r.PuedeDecidir)
                {
                    r.Mensaje = ReglasRetencionTodoJunto.MotivoBloqueo(r.Pedido, r.Plazo);
                    resultado.Add(r);
                }
            }
            return ResultadoRetencionPorFalta<List<RetencionPorFaltaDTO>>.Bien(resultado.OrderBy(r => r.Plazo).ToList());
        }

        public async Task<ResultadoRetencionPorFalta<RetencionPorFaltaDTO>> Decidir(string empresa, int pedido, DecidirRetencionPorFaltaDTO peticion, IPrincipal usuario)
        {
            string decision = EstadosRetencionPorFalta.Decision(peticion?.Decision);
            if (decision == null)
            {
                return ResultadoRetencionPorFalta<RetencionPorFaltaDTO>.Mal(HttpStatusCode.BadRequest,
                    "Indique la decisión: «ServirLoQueHay» (se sirve lo que hay) o «Esperar» (se espera a tenerlo completo).");
            }
            if (!await repositorio.TablaDisponible().ConfigureAwait(false))
            {
                return ResultadoRetencionPorFalta<RetencionPorFaltaDTO>.Mal(HttpStatusCode.ServiceUnavailable, MENSAJE_SIN_SCRIPT);
            }
            string emp = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            RetencionPorFaltaDTO retencion = await repositorio.LeerPendiente(emp, pedido).ConfigureAwait(false);
            if (retencion == null)
            {
                return ResultadoRetencionPorFalta<RetencionPorFaltaDTO>.Mal(HttpStatusCode.Conflict,
                    $"El pedido {pedido} no está retenido por falta (o ya se ha decidido).");
            }
            if (!PuedeDecidir(usuario, leerComerciales?.Invoke(emp, pedido)))
            {
                return ResultadoRetencionPorFalta<RetencionPorFaltaDTO>.Mal(HttpStatusCode.Forbidden,
                    "Solo puede decidirlo quien hizo el pedido, su vendedor, Almacén, Administración, Dirección o Informática.");
            }
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario?.Identity?.Name);
            if (decision == EstadosRetencionPorFalta.SERVIR_LO_QUE_HAY)
            {
                if (!await repositorio.ServirLoQueHay(retencion.Id, quien).ConfigureAwait(false))
                {
                    return YaDecidida(pedido);
                }
                AvisosRetencionPorFalta.Packing(avisos, retencion.Picking);
                return Hecha(retencion, decision, quien, $"El pedido {pedido} sigue al packing sin lo que falta (queda pendiente para otra entrega).");
            }
            ResultadoEsperar esperar = await repositorio.Esperar(retencion, quien, false).ConfigureAwait(false);
            if (esperar.YaDecidida)
            {
                return YaDecidida(pedido);
            }
            if (esperar.PickingSinTerminar)
            {
                return ResultadoRetencionPorFalta<RetencionPorFaltaDTO>.Mal(HttpStatusCode.Conflict,
                    $"El picking {retencion.Picking} todavía se está recogiendo: decida «Esperar» cuando el almacén lo termine (o «ServirLoQueHay» ya).");
            }
            AvisosRetencionPorFalta.Packing(avisos, retencion.Picking);
            return Hecha(retencion, decision, quien, MensajeEsperar(pedido, esperar));
        }

        internal static string MensajeEsperar(int pedido, ResultadoEsperar esperar) =>
            $"El pedido {pedido} sale del picking y espera a tenerlo completo. " +
            (esperar.UnidadesAPendienteDeUbicar > 0
                ? $"Lo ya cogido ({esperar.UnidadesAPendienteDeUbicar} uds.) queda «pendiente de ubicar»: el almacén lo vuelve a ubicar con la PDA."
                : "No había nada cogido que recolocar.");

        private ResultadoRetencionPorFalta<RetencionPorFaltaDTO> Hecha(RetencionPorFaltaDTO retencion, string decision, string quien, string mensaje)
        {
            retencion.Estado = decision;
            retencion.UsuarioDecision = quien;
            retencion.FechaDecision = ahora();
            retencion.PuedeDecidir = false;
            retencion.Mensaje = mensaje;
            return ResultadoRetencionPorFalta<RetencionPorFaltaDTO>.Bien(retencion);
        }

        private static ResultadoRetencionPorFalta<RetencionPorFaltaDTO> YaDecidida(int pedido) =>
            ResultadoRetencionPorFalta<RetencionPorFaltaDTO>.Mal(HttpStatusCode.Conflict, $"La retención del pedido {pedido} ya se ha decidido.");

        /// <summary>
        /// El job: a las vencidas se les aplica «Esperar» (la opción por defecto) y se avisa a quien pasó el pedido y a Almacén. Las de un
        /// picking que todavía se está recogiendo se dejan para la siguiente pasada. Devuelve cuántas ha aplicado. Nunca lanza.
        /// </summary>
        public async Task<List<RetencionPorFaltaDTO>> AplicarVencidas()
        {
            var aplicadas = new List<RetencionPorFaltaDTO>();
            if (!await repositorio.TablaDisponible().ConfigureAwait(false))
            {
                return aplicadas;
            }
            foreach (RetencionPorFaltaDTO r in await repositorio.LeerVencidas(ahora()).ConfigureAwait(false) ?? new List<RetencionPorFaltaDTO>())
            {
                try
                {
                    ResultadoEsperar esperar = await repositorio.Esperar(r, RetencionesPorFaltaJobsService.USUARIO, true).ConfigureAwait(false);
                    if (esperar.Hecho)
                    {
                        r.Estado = EstadosRetencionPorFalta.ESPERAR;
                        r.PorPlazo = true;
                        r.Mensaje = MensajeEsperar(r.Pedido, esperar);
                        aplicadas.Add(r);
                        AvisosRetencionPorFalta.Packing(avisos, r.Picking);
                    }
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Todo junto #858] No se ha podido aplicar «Esperar» al pedido {r.Pedido}: {ex.Message}", ex));
                }
            }
            return aplicadas;
        }

        /// <summary>El aviso de una retención a la que se ha aplicado «Esperar» por plazo.</summary>
        public static NotificacionPushDTO AvisoVencida(RetencionPorFaltaDTO r) => new NotificacionPushDTO
        {
            Titulo = $"Pedido {r.Pedido}: espera a tenerlo completo",
            Cuerpo = $"Nadie decidió a tiempo qué hacer con el pedido {r.Pedido} (cliente {r.Cliente?.Trim()}), «todo junto» y con falta de {r.Productos}: " +
                $"se espera a tenerlo completo. {r.Mensaje}",
            Tipo = ReglasRetencionTodoJunto.TIPO_AVISO_VENCIDA,
            Datos = new Dictionary<string, string>
            {
                ["tipo"] = ReglasRetencionTodoJunto.TIPO_AVISO_VENCIDA,
                ["empresa"] = r.Empresa?.Trim(),
                ["pedido"] = r.Pedido.ToString(CultureInfo.InvariantCulture)
            }
        };
    }

    /// <summary>NestoAPI#858 punto 4: el job pedidos-retenidos-por-falta (L-V cada 15 min de 8:00 a 18:45).</summary>
    public static class RetencionesPorFaltaJobsService
    {
        public const string ID_JOB = "pedidos-retenidos-por-falta";
        public const string CRON = "*/15 8-18 * * 1-5";
        public const string USUARIO = "NestoAPI (plazo todo junto)";

        public static async Task AplicarVencidas()
        {
            try
            {
                using (var repositorio = new RepositorioRetencionesPorFaltaSql())
                {
                    var servicio = new ServicioRetencionesPorFalta(repositorio, (e, p) => LeerComerciales(e, p), avisos: new AvisosAlmacenSignalR());
                    List<RetencionPorFaltaDTO> aplicadas = await servicio.AplicarVencidas().ConfigureAwait(false);
                    if (!aplicadas.Any())
                    {
                        return;
                    }
                    var notificaciones = new ServicioNotificacionesPush();
                    foreach (RetencionPorFaltaDTO r in aplicadas)
                    {
                        try
                        {
                            NotificacionPushDTO aviso = ServicioRetencionesPorFalta.AvisoVencida(r);
                            ComercialesPedido comerciales = LeerComerciales(r.Empresa, r.Pedido);
                            foreach (string usuario in Models.Picking.AvisoFaltasPicking.Destinatarios(comerciales).BuzonNesto)
                            {
                                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, aviso).ConfigureAwait(false);
                            }
                            string app = Models.Picking.AvisoFaltasPicking.Destinatarios(comerciales).UsuarioNestoApp;
                            if (!string.IsNullOrWhiteSpace(app))
                            {
                                _ = await notificaciones.EnviarAUsuario(app, Constantes.Aplicaciones.NESTO_APP, aviso).ConfigureAwait(false);
                            }
                            await new PedidosVenta.Retencion.AvisadorRetencionAlmacen().Avisar(aviso).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            ElmahHelper.Log(new Exception($"[Todo junto #858] No se ha podido avisar del «Esperar» por plazo del pedido {r.Pedido}: {ex.Message}", ex));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Todo junto #858] El job {ID_JOB} ha fallado: {ex.Message}", ex));
            }
        }

        internal static ComercialesPedido LeerComerciales(string empresa, int pedido)
        {
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return new LectorComercialesPedido(db).Leer(empresa, pedido);
            }
        }
    }

    /// <summary>NestoAPI#858: la guarda de packing, albarán, etiqueta y Facturar rutas. Sin tabla o si falla la lectura, null (como antes).</summary>
    public static class GuardiaRetencionPorFalta
    {
        internal static Func<string, int, Task<string>> Leer = MotivoSql;

        public static Task<string> Motivo(string empresa, int pedido) => Leer(empresa, pedido);

        /// <summary>Para el packing: de esos pedidos, los retenidos por falta (pedido → plazo). Sin tabla o si falla, ninguno.</summary>
        internal static Func<string, IReadOnlyCollection<int>, Task<Dictionary<int, DateTime>>> PendientesDe = PendientesDeSql;

        private static async Task<Dictionary<int, DateTime>> PendientesDeSql(string empresa, IReadOnlyCollection<int> pedidos)
        {
            var resultado = new Dictionary<int, DateTime>();
            if (pedidos == null || pedidos.Count == 0)
            {
                return resultado;
            }
            try
            {
                using (var repositorio = new RepositorioRetencionesPorFaltaSql())
                {
                    if (!await repositorio.TablaDisponible().ConfigureAwait(false))
                    {
                        return resultado;
                    }
                    string emp = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
                    foreach (RetencionPorFaltaDTO r in (await repositorio.LeerPendientes().ConfigureAwait(false))
                        .Where(r => string.Equals(r.Empresa?.Trim(), emp, StringComparison.OrdinalIgnoreCase) && pedidos.Contains(r.Pedido)))
                    {
                        resultado[r.Pedido] = r.Plazo;
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Todo junto #858] No se han podido leer los pedidos retenidos por falta del packing: {ex.Message}", ex));
            }
            return resultado;
        }

        /// <summary>Marca en el packing los pedidos retenidos por falta (Ariadna los pinta y no deja empaquetarlos).</summary>
        internal static async Task MarcarPacking(Models.PreparacionAlmacen.PackingAlmacenDTO packing)
        {
            List<Models.PreparacionAlmacen.PedidoPackingAlmacenDTO> pedidos = packing?.Entregas?.SelectMany(e => e.Pedidos).ToList();
            if (pedidos == null || !pedidos.Any())
            {
                return;
            }
            Dictionary<int, DateTime> retenidos = await PendientesDe(packing.Empresa, pedidos.Select(p => p.Pedido).Distinct().ToList()).ConfigureAwait(false)
                ?? new Dictionary<int, DateTime>();
            foreach (Models.PreparacionAlmacen.PedidoPackingAlmacenDTO pedido in pedidos.Where(p => retenidos.ContainsKey(p.Pedido)))
            {
                pedido.RetenidoPorFalta = true;
                pedido.AvisoRetencion = ReglasRetencionTodoJunto.TEXTO_PACKING;
                pedido.PlazoRetencion = retenidos[pedido.Pedido];
            }
        }

        private static async Task<string> MotivoSql(string empresa, int pedido)
        {
            try
            {
                using (var repositorio = new RepositorioRetencionesPorFaltaSql())
                {
                    if (!await repositorio.TablaDisponible().ConfigureAwait(false))
                    {
                        return null;
                    }
                    RetencionPorFaltaDTO r = await repositorio.LeerPendiente(
                        string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim(), pedido).ConfigureAwait(false);
                    return r == null ? null : ReglasRetencionTodoJunto.MotivoBloqueo(pedido, r.Plazo);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Todo junto #858] No se ha podido mirar si el pedido {pedido} está retenido por falta: {ex.Message}", ex));
                return null;
            }
        }
    }

    public class RepositorioRetencionesPorFaltaSql : IRepositorioRetencionesPorFalta, IDisposable
    {
        internal const string SQL_TABLA = "SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.PedidosRetenidosPorFalta', N'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string COLUMNAS = @"r.Id, RTRIM(r.Empresa) AS Empresa, r.Pedido, r.Picking, RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(c.Contacto) AS Contacto,
       RTRIM(cl.Nombre) AS NombreCliente, r.Productos, r.Fecha, r.Plazo, RTRIM(r.Estado) AS Estado, RTRIM(r.UsuarioDecision) AS UsuarioDecision,
       r.FechaDecision, r.PorPlazo
FROM dbo.PedidosRetenidosPorFalta r
     LEFT JOIN CabPedidoVta c ON c.Empresa = r.Empresa AND c.[Número] = r.Pedido
     LEFT JOIN Clientes cl ON cl.Empresa = c.Empresa AND cl.[Nº Cliente] = c.[Nº Cliente] AND cl.Contacto = c.Contacto";

        internal const string SQL_PENDIENTE = "SELECT TOP 1 " + COLUMNAS + " WHERE r.Empresa = @p0 AND r.Pedido = @p1 AND r.Estado = 'Pendiente' ORDER BY r.Id";
        internal const string SQL_PENDIENTES = "SELECT " + COLUMNAS + " WHERE r.Estado = 'Pendiente'";
        internal const string SQL_VENCIDAS = "SELECT " + COLUMNAS + " WHERE r.Estado = 'Pendiente' AND r.Plazo <= @p0";

        internal const string SQL_DECIDIR = @"UPDATE dbo.PedidosRetenidosPorFalta SET Estado = @p1, UsuarioDecision = @p2, FechaDecision = GETDATE(), PorPlazo = @p3
WHERE Id = @p0 AND Estado = 'Pendiente'";

        // ¿Está terminado el picking en Ariadna? (la misma marca que el Terminar: PreparacionTerminadas)
        internal const string SQL_LINEAS_PEDIDO_PICKING = @"
SELECT l.[Nº Orden] FROM LinPedidoVta l WITH (UPDLOCK)
WHERE l.Empresa = @p0 AND l.[Número] = @p1 AND l.Picking = @p2 AND l.Estado = 1";

        internal const string SQL_RESERVAS_LINEA = "SELECT [NºOrden] AS Ubicacion, CAST(ABS(Cantidad) AS int) AS Cantidad FROM Ubicaciones WITH (UPDLOCK) WHERE [NºOrdenVta] = @p0 AND Estado = 3";

        internal const string SQL_QUITAR_PICKING = @"UPDATE LinPedidoVta SET Picking = 0, Usuario = @p1, [Fecha Modificación] = GETDATE() WHERE [Nº Orden] = @p0 AND Picking > 0";

        internal const string SQL_RASTRO = "INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (N'LinPedidoVta', @p0, @p1, @p2)";

        private readonly NVEntities db;

        public RepositorioRetencionesPorFaltaSql()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
        }

        public async Task<bool> TablaDisponible() => await db.Database.SqlQuery<bool>(SQL_TABLA).FirstAsync().ConfigureAwait(false);

        public async Task<RetencionPorFaltaDTO> LeerPendiente(string empresa, int pedido) =>
            (await db.Database.SqlQuery<FilaRetencion>(SQL_PENDIENTE, new SqlParameter("@p0", empresa), new SqlParameter("@p1", pedido))
                .ToListAsync().ConfigureAwait(false)).Select(ADTO).FirstOrDefault();

        public async Task<List<RetencionPorFaltaDTO>> LeerPendientes() =>
            (await db.Database.SqlQuery<FilaRetencion>(SQL_PENDIENTES).ToListAsync().ConfigureAwait(false)).Select(ADTO).ToList();

        public async Task<List<RetencionPorFaltaDTO>> LeerVencidas(DateTime ahora) =>
            (await db.Database.SqlQuery<FilaRetencion>(SQL_VENCIDAS, new SqlParameter("@p0", ahora)).ToListAsync().ConfigureAwait(false))
                .Select(ADTO).ToList();

        private class FilaRetencion
        {
            public int Id { get; set; }
            public string Empresa { get; set; }
            public int Pedido { get; set; }
            public int Picking { get; set; }
            public string Cliente { get; set; }
            public string Contacto { get; set; }
            public string NombreCliente { get; set; }
            public string Productos { get; set; }
            public DateTime Fecha { get; set; }
            public DateTime Plazo { get; set; }
            public string Estado { get; set; }
            public string UsuarioDecision { get; set; }
            public DateTime? FechaDecision { get; set; }
            public bool PorPlazo { get; set; }
        }

        private static RetencionPorFaltaDTO ADTO(FilaRetencion f) => new RetencionPorFaltaDTO
        {
            Id = f.Id, Empresa = f.Empresa, Pedido = f.Pedido, Picking = f.Picking, Cliente = f.Cliente, Contacto = f.Contacto,
            NombreCliente = f.NombreCliente, Productos = f.Productos, Fecha = f.Fecha, Plazo = f.Plazo, Estado = f.Estado,
            UsuarioDecision = f.UsuarioDecision, FechaDecision = f.FechaDecision, PorPlazo = f.PorPlazo
        };

        public async Task<bool> ServirLoQueHay(int id, string usuario) =>
            await db.Database.ExecuteSqlCommandAsync(SQL_DECIDIR, new SqlParameter("@p0", id), new SqlParameter("@p1", EstadosRetencionPorFalta.SERVIR_LO_QUE_HAY),
                new SqlParameter("@p2", Recortar(usuario)), new SqlParameter("@p3", false)).ConfigureAwait(false) == 1;

        public async Task<ResultadoEsperar> Esperar(RetencionPorFaltaDTO retencion, string usuario, bool porPlazo)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                var tx = new TransaccionSalidaSql(db);
                if (!await tx.EstaTerminada(retencion.Empresa, CasadorEscaneos.ORIGEN_PICKING, retencion.Picking).ConfigureAwait(false))
                {
                    transaccion.Rollback();
                    return new ResultadoEsperar { PickingSinTerminar = true };
                }
                int filas = await db.Database.ExecuteSqlCommandAsync(SQL_DECIDIR, new SqlParameter("@p0", retencion.Id),
                    new SqlParameter("@p1", EstadosRetencionPorFalta.ESPERAR), new SqlParameter("@p2", Recortar(usuario)),
                    new SqlParameter("@p3", porPlazo)).ConfigureAwait(false);
                if (filas != 1)
                {
                    transaccion.Rollback();
                    return new ResultadoEsperar { YaDecidida = true };
                }
                var resultado = new ResultadoEsperar { Hecho = true };
                List<int> lineas = await db.Database.SqlQuery<int>(SQL_LINEAS_PEDIDO_PICKING, new SqlParameter("@p0", retencion.Empresa),
                    new SqlParameter("@p1", retencion.Pedido), new SqlParameter("@p2", retencion.Picking)).ToListAsync().ConfigureAwait(false);
                foreach (int linea in lineas)
                {
                    List<ReservaLinea> reservas = await db.Database.SqlQuery<ReservaLinea>(SQL_RESERVAS_LINEA, new SqlParameter("@p0", linea))
                        .ToListAsync().ConfigureAwait(false);
                    foreach (ReservaLinea reserva in reservas)
                    {
                        // Lo cogido está en el carro, no en el hueco: «pendiente de ubicar», como una falta (TransaccionSalidaSql.QuitarDeLaReserva)
                        _ = await db.Database.ExecuteSqlCommandAsync(TransaccionSalidaSql.SQL_RESERVA_A_PENDIENTE_DE_UBICAR,
                            new SqlParameter("@p0", reserva.Ubicacion), new SqlParameter("@p1", reserva.Cantidad),
                            new SqlParameter("@p2", Recortar(usuario))).ConfigureAwait(false);
                        resultado.UnidadesAPendienteDeUbicar += reserva.Cantidad;
                    }
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_QUITAR_PICKING, new SqlParameter("@p0", linea),
                        new SqlParameter("@p1", Recortar(usuario))).ConfigureAwait(false);
                    resultado.LineasSacadas++;
                }
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_RASTRO,
                    new SqlParameter("@p0", $"Pedido {retencion.Pedido} retenido por falta en el picking {retencion.Picking} ({retencion.Productos})"),
                    new SqlParameter("@p1", $"Esperar{(porPlazo ? " (por plazo)" : "")}: {resultado.LineasSacadas} líneas sacadas del picking, " +
                        $"{resultado.UnidadesAPendienteDeUbicar} uds. a pendiente de ubicar (NestoAPI#858)"),
                    new SqlParameter("@p2", Recortar(usuario))).ConfigureAwait(false);
                transaccion.Commit();
                return resultado;
            }
        }

        private class ReservaLinea
        {
            public int Ubicacion { get; set; }
            public int Cantidad { get; set; }
        }

        private static string Recortar(string usuario)
        {
            string limpio = usuario?.Trim() ?? "NestoAPI";
            return limpio.Length > 30 ? limpio.Substring(0, 30) : limpio;
        }

        public void Dispose() => db.Dispose();
    }
}
