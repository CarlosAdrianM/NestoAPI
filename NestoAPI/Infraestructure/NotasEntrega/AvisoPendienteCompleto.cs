using Hangfire;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    /// <summary>
    /// NestoAPI#688 (decisiones 3, 4 y 5 de Carlos, 21/10/26): las notas de entrega automáticas que nacieron sin fecha
    /// (casilla «¿Servir lo pendiente en cuanto haya stock?» sin marcar) se quedan en carpeta hasta que alguien pone la fecha.
    /// Cuando TODO lo pendiente de una nota ya tiene stock en su almacén, se avisa UNA vez:
    /// <list type="bullet">
    /// <item>a quien hizo el pedido: buzón de Nesto (la campana se entera por SignalR, AvisosTiempoReal);</item>
    /// <item>a su vendedor, si es otra persona: push de NestoApp (que también lo deja en su buzón).</item>
    /// </list>
    /// Lo lanza cada hora en horario laboral el job «aviso-pendiente-completo» (cubre cualquier entrada de stock, también la
    /// del Nesto viejo); nada de sondeo en los clientes. Se marca CabPedidoVta.AvisoPendienteCompleto antes de avisar: un
    /// aviso que falle se registra y no se repite. Sin el script de #688 no encuentra notas y no hace nada.
    /// </summary>
    public class AvisoPendienteCompleto
    {
        public const string TIPO_NOTIFICACION = "PendienteCompleto";
        private const string DOMINIO = "NUEVAVISION\\";
        /// <summary>Horario laboral: de 8:00 a 19:59 (el cron ya va de lunes a viernes; los festivos se saltan aquí).</summary>
        internal const int HORA_INICIO = 8;
        internal const int HORA_FIN = 19;

        private readonly IRepositorioServirPendienteAlHaberStock repositorio;
        private readonly Func<IGestorStocks> stocks;
        private readonly ILectorComercialesPedido lector;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<DateTime> reloj;
        private readonly Func<DateTime, bool> esLaborable;
        private readonly Action<Exception> registrar;

        internal AvisoPendienteCompleto(IRepositorioServirPendienteAlHaberStock repositorio, Func<IGestorStocks> stocks,
            ILectorComercialesPedido lector, IServicioNotificacionesPush notificaciones, Func<DateTime> reloj,
            Func<DateTime, bool> esLaborable, Action<Exception> registrar = null)
        {
            this.repositorio = repositorio;
            this.stocks = stocks;
            this.lector = lector;
            this.notificaciones = notificaciones;
            this.reloj = reloj;
            this.esLaborable = esLaborable;
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex, "Sistema (aviso pendiente completo)"));
        }

        internal static bool EnHorarioLaboral(DateTime ahora, Func<DateTime, bool> esLaborable) =>
            ahora.DayOfWeek != DayOfWeek.Saturday && ahora.DayOfWeek != DayOfWeek.Sunday
            && ahora.Hour >= HORA_INICIO && ahora.Hour <= HORA_FIN
            && esLaborable(ahora.Date);

        /// <summary>Una pasada. Devuelve cuántas notas se han avisado.</summary>
        public async Task<int> Ejecutar()
        {
            DateTime ahora = reloj();
            if (!EnHorarioLaboral(ahora, esLaborable))
            {
                return 0;
            }
            List<NotaPendienteSinAvisar> notas = repositorio.LeerNotasSinAvisar();
            if (!notas.Any())
            {
                return 0;
            }
            IGestorStocks gestorStocks = stocks();
            int avisadas = 0;
            foreach (IGrouping<string, NotaPendienteSinAvisar> porEmpresa in notas.GroupBy(n => n.Empresa))
            {
                List<LineaNotaPendiente> lineas = repositorio.LeerLineas(porEmpresa.Key, porEmpresa.Select(n => n.Nota));
                foreach (NotaPendienteSinAvisar nota in porEmpresa)
                {
                    try
                    {
                        List<LineaNotaPendiente> suyas = lineas.Where(l => l.Nota == nota.Nota).ToList();
                        if (!TodoTieneStock(suyas, gestorStocks))
                        {
                            continue;
                        }
                        if (!repositorio.MarcarAvisada(nota.Empresa, nota.Nota, ahora))
                        {
                            continue; // otra pasada (u otra persona) ya la ha marcado
                        }
                        await Avisar(nota).ConfigureAwait(false);
                        avisadas++;
                    }
                    catch (Exception ex)
                    {
                        registrar(new Exception($"[Aviso pendiente completo #688] Nota {nota.Nota} (pedido {nota.PedidoOrigen}): {ex.Message}", ex));
                    }
                }
            }
            return avisadas;
        }

        /// <summary>
        /// Decisión 4: solo cuando TODO lo pendiente tiene stock. Por producto y almacén, el stock tiene que cubrir todo lo
        /// pendiente de entregar de ese almacén (que ya incluye las líneas de la nota): es el «verde» del correo de pedido.
        /// Conservador a propósito: mejor avisar una pasada más tarde que avisar de algo que el picking aún no puede sacar.
        /// Sin líneas de producto, no hay nada que avisar.
        /// </summary>
        internal static bool TodoTieneStock(IEnumerable<LineaNotaPendiente> lineas, IGestorStocks stocks)
        {
            var grupos = (lineas ?? Enumerable.Empty<LineaNotaPendiente>())
                .Where(l => !string.IsNullOrWhiteSpace(l.Producto) && l.Cantidad > 0)
                .GroupBy(l => new { Producto = l.Producto.Trim(), Almacen = (l.Almacen ?? Constantes.Almacenes.ALGETE).Trim() })
                .ToList();
            if (!grupos.Any())
            {
                return false;
            }
            return grupos.All(g =>
            {
                int stock = stocks.Stock(g.Key.Producto, g.Key.Almacen);
                int pendientes = stocks.UnidadesPendientesEntregarAlmacen(g.Key.Producto, g.Key.Almacen);
                return stock >= g.Sum(l => l.Cantidad) && stock - pendientes >= 0;
            });
        }

        private async Task Avisar(NotaPendienteSinAvisar nota)
        {
            ComercialesPedido comerciales = nota.PedidoOrigen.HasValue ? lector.Leer(nota.Empresa, nota.PedidoOrigen.Value) : null;
            NotificacionPushDTO notificacion = Componer(nota, comerciales);
            foreach (string usuario in UsuariosBuzon(comerciales))
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
            }
            foreach (string vendedor in VendedoresAPush(comerciales))
            {
                _ = await notificaciones.EnviarAVendedor(nota.Empresa, vendedor, notificacion).ConfigureAwait(false);
            }
        }

        /// <summary>Quien hizo el pedido (buzón de Nesto). Si no se sabe, los usuarios de sus comerciales.</summary>
        internal static List<string> UsuariosBuzon(ComercialesPedido comerciales)
        {
            if (comerciales == null)
            {
                return new List<string>();
            }
            if (!string.IsNullOrWhiteSpace(comerciales.UsuarioPedido))
            {
                return new List<string> { DOMINIO + ComercialesPedido.SinDominio(comerciales.UsuarioPedido) };
            }
            return comerciales.UsuariosBuzon();
        }

        /// <summary>
        /// Decisión 3: los vendedores reales del pedido (no el general) que NO son quien lo hizo (si lo hizo él, ya le llega
        /// al buzón). Su push de NestoApp también lo guarda en su buzón.
        /// </summary>
        internal static List<string> VendedoresAPush(ComercialesPedido comerciales)
        {
            if (comerciales == null)
            {
                return new List<string>();
            }
            string quienLoHizo = ComercialesPedido.SinDominio(comerciales.UsuarioPedido);
            return (comerciales.Comerciales ?? new List<ComercialPedido>())
                .Where(ComercialesPedido.EsComercialReal)
                .Where(c => string.IsNullOrWhiteSpace(quienLoHizo)
                    || !(c.Usuarios ?? new List<string>()).Any(u => string.Equals(ComercialesPedido.SinDominio(u), quienLoHizo, StringComparison.OrdinalIgnoreCase)))
                .Select(c => c.Vendedor.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static NotificacionPushDTO Componer(NotaPendienteSinAvisar nota, ComercialesPedido comerciales)
        {
            string numeroCliente = (comerciales?.Cliente ?? nota.Cliente)?.Trim();
            string nombre = comerciales?.NombreCliente?.Trim();
            string cliente = string.IsNullOrWhiteSpace(nombre) ? numeroCliente : $"{numeroCliente} - {nombre}";
            string pedido = nota.PedidoOrigen.HasValue ? $" del pedido {nota.PedidoOrigen}" : string.Empty;
            var datos = new Dictionary<string, string>
            {
                ["tipo"] = TIPO_NOTIFICACION,
                ["empresa"] = nota.Empresa?.Trim(),
                ["nota"] = nota.Nota.ToString(),
                ["cliente"] = numeroCliente
            };
            if (nota.PedidoOrigen.HasValue)
            {
                datos["pedido"] = nota.PedidoOrigen.Value.ToString();
            }
            return new NotificacionPushDTO
            {
                Titulo = $"Ya hay stock de todo lo pendiente de la nota {nota.Nota}",
                Cuerpo = $"Todo lo pendiente{pedido} (nota de entrega {nota.Nota}, cliente {cliente}) ya tiene stock. " +
                    "Si el cliente lo quiere ya, póngale fecha de entrega a la nota o marque «¿Servir lo pendiente en cuanto haya stock?».",
                Tipo = TIPO_NOTIFICACION,
                Datos = datos
            };
        }
    }

    /// <summary>NestoAPI#688: el job horario «aviso-pendiente-completo».</summary>
    public static class AvisoPendienteCompletoJobsService
    {
        public const string ID_JOB = "aviso-pendiente-completo";
        /// <summary>Cada hora en punto, de 8:00 a 19:00, de lunes a viernes.</summary>
        public const string CRON = "0 8-19 * * 1-5";

        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static async Task Procesar()
        {
            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    var aviso = new AvisoPendienteCompleto(new RepositorioServirPendienteAlHaberStock(), () => new GestorStocks(),
                        new LectorComercialesPedido(db), new ServicioNotificacionesPush(), () => DateTime.Now,
                        Novedades.RecordatoriosRespuestaNovedades.EsLaborableOficina);
                    _ = await aviso.Ejecutar().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Aviso pendiente completo #688] Error en el job: " + ex.Message, ex), "Sistema (aviso pendiente completo)");
                throw;
            }
        }
    }
}
