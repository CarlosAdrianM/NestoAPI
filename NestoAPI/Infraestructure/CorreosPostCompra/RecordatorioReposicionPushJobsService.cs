using Hangfire;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>Nivel del interruptor de la push de reposición (NestoAPI#662), independiente del correo de #532.</summary>
    public enum ModoRecordatorioReposicionPush
    {
        /// <summary>No hace nada. Es el valor por defecto (sin fila o cualquier cosa no reconocida).</summary>
        Apagado,
        /// <summary>Calcula a quién le llegaría la push hoy y se lo manda SOLO a Informática. No envía nada a clientes.</summary>
        Sombra,
        /// <summary>Envía la push a los clientes.</summary>
        Activo
    }

    /// <summary>NestoAPI#662: una push de reposición (enviada o, en sombra, la que se habría enviado).</summary>
    public class PushReposicionDTO
    {
        public string Cliente { get; set; }
        public string Nombre { get; set; }
        public int Dispositivos { get; set; }
        public NotificacionPushDTO Notificacion { get; set; }
        public List<CandidatoReposicionDTO> Productos { get; set; } = new List<CandidatoReposicionDTO>();
    }

    /// <summary>NestoAPI#662: lo que ha hecho una pasada del job (para los tests y el correo sombra).</summary>
    public class ResultadoRecordatorioReposicionPushDTO
    {
        public ModoRecordatorioReposicionPush Modo { get; set; }
        public DateTime Ahora { get; set; }
        /// <summary>Por qué no se ha mirado a nadie (fuera de día u hora, víspera de festivo...). Null si se ha calculado.</summary>
        public string MotivoNoSeEnvia { get; set; }
        public List<PushReposicionDTO> Pushes { get; set; } = new List<PushReposicionDTO>();
        /// <summary>Clientes con la app que se quedan fuera por la regla de una push cada 7 días.</summary>
        public List<string> ConPushReciente { get; set; } = new List<string>();
        /// <summary>Clientes a los que les tocaría, pero sin dispositivo activo de NestoTiendas con consejos.</summary>
        public List<string> SinDispositivoConConsejos { get; set; } = new List<string>();
        public ResultadoRecordatorioReposicionDTO Calculo { get; set; }
    }

    /// <summary>
    /// NestoAPI#662: segundo canal del recordatorio de reposición (#532): una push a los clientes que tienen NestoTiendas.
    /// El cálculo es el mismo (<see cref="ISelectorRecordatoriosReposicion.CalcularParaClientes"/>, con sus exclusiones:
    /// pedido pendiente, sustituto, sin stock, cliente de baja), sin grupo de control (son unas decenas de clientes: se
    /// mide a mano con Scripts/Issue662_MedicionReposicionPush.sql) y sin exigir correo.
    ///
    /// <para>Reglas para no agobiar, todas aquí y no en la app:</para>
    /// <list type="bullet">
    /// <item>Solo de lunes a jueves y antes de la hora límite (<c>RecordatorioReposicionHoraLimite</c>, 11:00 por defecto):
    /// lo que se pida tiene que salir hoy para que «lo tienes mañana» sea verdad. Ni hoy ni mañana pueden ser festivo
    /// en Algete (<see cref="GestorFestivos.EsFestivo"/>; donde su lista no llega, solo cuentan los fines de semana).</item>
    /// <item>Solo a clientes con dispositivo activo de NestoTiendas que no hayan apagado los consejos (RecibirConsejos).</item>
    /// <item>Una push de reposición por cliente cada 7 días como mucho, aunque toquen productos nuevos: se mira en el
    /// buzón (<c>NotificacionesBuzon</c>, por Cliente, con Datos de tipo «reposicion»), que es donde ya queda cada envío.</item>
    /// <item>Hasta 3 productos, los de más importe habitual.</item>
    /// </list>
    ///
    /// <para>Interruptor propio: <c>RecordatorioReposicionPush</c> bajo «(defecto)». El correo semanal de #532 y su
    /// parámetro no cambian.</para>
    /// </summary>
    public static class RecordatorioReposicionPushJobsService
    {
        private const string EMPRESA = Constantes.Empresas.EMPRESA_POR_DEFECTO;
        internal const string VALOR_SOMBRA = "Sombra";
        internal const string VALOR_ACTIVO = "Activo";
        internal const string TIPO = "reposicion";
        internal const string TITULO = "Nueva Visión";
        internal const int DIAS_ENTRE_PUSH = 7;
        internal static readonly TimeSpan HORA_LIMITE_POR_DEFECTO = new TimeSpan(11, 0, 0);

        internal const string MOTIVO_DIA = "Solo se manda de lunes a jueves.";
        internal const string MOTIVO_HORA = "Ya ha pasado la hora límite: lo que se pida no sale hoy.";
        internal const string MOTIVO_FESTIVO = "Hoy o mañana es festivo: no saldría a tiempo.";

        private static readonly CultureInfo ES = CultureInfo.GetCultureInfo("es-ES");

        // Sin reintentos: si falla, mañana vuelve a pasar. Y nunca dos a la vez.
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static async Task Procesar()
        {
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    _ = await Procesar(
                        new LectorParametrosUsuario(),
                        new ServicioNotificacionesPush(),
                        new ServicioCorreoElectronico(),
                        (clientes, consumibles, hoy) => new SelectorRecordatoriosReposicion(db).CalcularParaClientes(EMPRESA, hoy, consumibles, clientes),
                        () => ClientesConConsejos(db),
                        (clientes, desde) => ClientesConPushDesde(db, clientes, desde),
                        dia => GestorFestivos.EsFestivo(dia, Constantes.Almacenes.ALGETE),
                        DateTime.Now).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Push reposición #662] Error en el job: " + ex.Message, ex));
                throw;
            }
        }

        /// <summary>Núcleo del job, con las dependencias inyectadas para testearlo sin BD, Firebase ni SMTP.</summary>
        /// <param name="calcular">(clientes, consumibles, hoy) → el cálculo de #532 acotado a esos clientes.</param>
        /// <param name="clientesConConsejos">Clientes con algún dispositivo activo de NestoTiendas con RecibirConsejos
        /// (acota el cálculo: unas decenas de consultas por cliente en vez de recorrer 24 meses de toda la empresa).</param>
        /// <param name="clientesConPushDesde">(clientes, desde) → los que ya tienen una push de reposición desde ese día.</param>
        /// <param name="esFestivo">Día → festivo en el almacén que sirve.</param>
        internal static async Task<ResultadoRecordatorioReposicionPushDTO> Procesar(ILectorParametrosUsuario lector,
            IServicioNotificacionesPush push,
            IServicioCorreoElectronico servicioCorreo,
            Func<IReadOnlyCollection<string>, string, DateTime, Task<ResultadoRecordatorioReposicionDTO>> calcular,
            Func<Task<List<string>>> clientesConConsejos,
            Func<IReadOnlyCollection<string>, DateTime, Task<ISet<string>>> clientesConPushDesde,
            Func<DateTime, bool> esFestivo,
            DateTime ahora)
        {
            var resultado = new ResultadoRecordatorioReposicionPushDTO { Modo = LeerModo(lector), Ahora = ahora };
            if (resultado.Modo == ModoRecordatorioReposicionPush.Apagado)
            {
                return resultado;
            }

            resultado.MotivoNoSeEnvia = MotivoNoSeEnvia(ahora, LeerHoraLimite(lector), esFestivo);
            if (resultado.MotivoNoSeEnvia != null)
            {
                return resultado;
            }

            List<string> conApp = ((await clientesConConsejos().ConfigureAwait(false)) ?? new List<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (conApp.Count == 0)
            {
                return resultado;
            }

            // Una push cada 7 días: con la de hace 7 días ya se puede otra; con la de hace 6, no
            DateTime desde = ahora.Date.AddDays(-(DIAS_ENTRE_PUSH - 1));
            ISet<string> recientes = await clientesConPushDesde(conApp, desde).ConfigureAwait(false)
                ?? new HashSet<string>();
            var recientesSinEspacios = new HashSet<string>(recientes.Select(r => r?.Trim()), StringComparer.OrdinalIgnoreCase);
            resultado.ConPushReciente = conApp.Where(recientesSinEspacios.Contains).ToList();
            List<string> elegibles = conApp.Where(c => !recientesSinEspacios.Contains(c)).ToList();
            if (elegibles.Count == 0)
            {
                return resultado;
            }

            resultado.Calculo = await calcular(elegibles, RecordatorioReposicionJobsService.LeerConsumibles(lector), ahora.Date)
                .ConfigureAwait(false) ?? new ResultadoRecordatorioReposicionDTO { Fecha = ahora.Date };

            // Sin grupo de control en este canal: si el cálculo trajera alguno, también se le avisa
            foreach (RecordatorioReposicionClienteDTO cliente in resultado.Calculo.SeAvisarian.Concat(resultado.Calculo.GrupoControl)
                .Where(c => c.Productos.Any()))
            {
                List<DispositivoNotificacion> dispositivos = (await push.ObtenerDispositivosCliente(EMPRESA, cliente.Cliente,
                        Constantes.Aplicaciones.NESTO_TIENDAS).ConfigureAwait(false) ?? new List<DispositivoNotificacion>())
                    .Where(d => d.Activo && d.RecibirConsejos)
                    .ToList();
                if (dispositivos.Count == 0)
                {
                    resultado.SinDispositivoConConsejos.Add(cliente.Cliente);
                    continue;
                }

                List<CandidatoReposicionDTO> productos = cliente.Productos
                    .OrderByDescending(p => p.ImporteHabitual)
                    .ThenBy(p => p.Producto, StringComparer.OrdinalIgnoreCase)
                    .Take(CalculadoraReposicion.MAXIMO_PRODUCTOS_POR_CORREO)
                    .ToList();
                var pushCliente = new PushReposicionDTO
                {
                    Cliente = cliente.Cliente,
                    Nombre = cliente.Nombre,
                    Dispositivos = dispositivos.Count,
                    Productos = productos,
                    Notificacion = Notificacion(productos)
                };

                if (resultado.Modo == ModoRecordatorioReposicionPush.Activo)
                {
                    try
                    {
                        _ = await push.EnviarConsejoACliente(EMPRESA, cliente.Cliente, pushCliente.Notificacion).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Un cliente que falla no deja sin push a los demás
                        ElmahHelper.Log(new Exception($"[Push reposición #662] Cliente {cliente.Cliente}: {ex.Message}", ex));
                        continue;
                    }
                }
                resultado.Pushes.Add(pushCliente);
            }

            if (resultado.Modo == ModoRecordatorioReposicionPush.Sombra)
            {
                using (MailMessage correo = ConstruirCorreoSombra(resultado))
                {
                    if (!servicioCorreo.EnviarCorreoSMTP(correo))
                    {
                        ElmahHelper.Log(new Exception($"[Push reposición #662] No se ha podido mandar el correo sombra ({resultado.Pushes.Count} push)."));
                    }
                }
            }
            return resultado;
        }

        /// <summary>Null si hoy, a esta hora, se puede mandar; si no, por qué no.</summary>
        internal static string MotivoNoSeEnvia(DateTime ahora, TimeSpan horaLimite, Func<DateTime, bool> esFestivo)
        {
            if (ahora.DayOfWeek < DayOfWeek.Monday || ahora.DayOfWeek > DayOfWeek.Thursday)
            {
                return MOTIVO_DIA;
            }
            if (ahora.TimeOfDay >= horaLimite)
            {
                return MOTIVO_HORA;
            }
            if (esFestivo != null && (esFestivo(ahora.Date) || esFestivo(ahora.Date.AddDays(1))))
            {
                return MOTIVO_FESTIVO;
            }
            return null;
        }

        /// <summary>Cualquier fallo al leer el parámetro cuenta como apagado.</summary>
        internal static ModoRecordatorioReposicionPush LeerModo(ILectorParametrosUsuario lector)
        {
            try
            {
                return InterpretarModo(lector.LeerParametro(EMPRESA, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO,
                    Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_PUSH));
            }
            catch
            {
                return ModoRecordatorioReposicionPush.Apagado;
            }
        }

        internal static ModoRecordatorioReposicionPush InterpretarModo(string valor)
        {
            string limpio = valor?.Trim();
            if (string.Equals(limpio, VALOR_ACTIVO, StringComparison.OrdinalIgnoreCase))
            {
                return ModoRecordatorioReposicionPush.Activo;
            }
            return string.Equals(limpio, VALOR_SOMBRA, StringComparison.OrdinalIgnoreCase)
                ? ModoRecordatorioReposicionPush.Sombra
                : ModoRecordatorioReposicionPush.Apagado;
        }

        /// <summary>«HH:mm». Sin fila, ilegible o con error: 11:00.</summary>
        internal static TimeSpan LeerHoraLimite(ILectorParametrosUsuario lector)
        {
            try
            {
                return InterpretarHoraLimite(lector.LeerParametro(EMPRESA, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO,
                    Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_HORA_LIMITE));
            }
            catch
            {
                return HORA_LIMITE_POR_DEFECTO;
            }
        }

        internal static TimeSpan InterpretarHoraLimite(string valor)
            => DateTime.TryParseExact(valor?.Trim(), new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out DateTime hora)
                ? hora.TimeOfDay
                : HORA_LIMITE_POR_DEFECTO;

        /// <summary>
        /// La push: título de la app y un texto de ayuda, nunca de venta (ni «oferta», ni «descuento», ni importes). Va de
        /// tú a propósito: es el tono de TNV. Datos para el aterrizaje en el carrito (TNV#87): tipo y
        /// «ref1:cant1;ref2:cant2» con la cantidad habitual.
        /// </summary>
        internal static NotificacionPushDTO Notificacion(IReadOnlyList<CandidatoReposicionDTO> productos)
            => new NotificacionPushDTO
            {
                Titulo = TITULO,
                Cuerpo = Cuerpo(productos.Select(p => p.NombreProducto ?? p.Producto).ToList()),
                Tipo = TIPO,
                Datos = new Dictionary<string, string>
                {
                    { "tipo", TIPO },
                    { "productos", string.Join(";", productos.Select(p => $"{p.Producto?.Trim()}:{Math.Max(1, p.CantidadHabitual)}")) }
                }
            };

        /// <summary>
        /// 1 producto: «Planifica para no quedarte sin {p1}»; 2: «Planifica tu semana: {p1} y {p2}»; 3: «Planifica tu
        /// semana: {p1}, {p2} y {p3}». Nombres en minúscula natural.
        /// </summary>
        internal static string Cuerpo(IReadOnlyList<string> nombres)
        {
            List<string> n = (nombres ?? new List<string>()).Select(NombreNatural).Where(x => x.Length > 0)
                .Take(CalculadoraReposicion.MAXIMO_PRODUCTOS_POR_CORREO).ToList();
            switch (n.Count)
            {
                case 0:
                    return "Planifica tu semana";
                case 1:
                    return $"Planifica para no quedarte sin {n[0]}";
                case 2:
                    return $"Planifica tu semana: {n[0]} y {n[1]}";
                default:
                    return $"Planifica tu semana: {n[0]}, {n[1]} y {n[2]}";
            }
        }

        /// <summary>
        /// Los nombres de Nesto vienen en mayúsculas («CHAMPÚ REPARADOR 1000 ML»): en la frase van en minúscula
        /// («champú reparador 1000 ml»), con sus tildes y sin espacios de más. No hay otro normalizador de nombres de
        /// producto en CorreosPostCompra (el de #532 es de nombres de pila).
        /// </summary>
        internal static string NombreNatural(string nombre)
            => string.Join(" ", (nombre ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                .ToLower(ES);

        /// <summary>Clientes (sin repetir) con algún dispositivo activo de NestoTiendas que quiere consejos.</summary>
        internal static async Task<List<string>> ClientesConConsejos(NVEntities db)
        {
            List<string> clientes = await db.DispositivosNotificaciones
                .Where(d => d.Aplicacion == Constantes.Aplicaciones.NESTO_TIENDAS && d.Activo && d.RecibirConsejos
                    && d.Empresa == EMPRESA && d.Cliente != null)
                .Select(d => d.Cliente)
                .Distinct()
                .ToListAsync().ConfigureAwait(false);
            return clientes.Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Los clientes que ya tienen una push de reposición en el buzón desde ese día (cuenta aunque la hayan borrado:
        /// les llegó). El buzón guarda una fila por usuario con su Cliente, y Datos es el JSON de la push.
        /// </summary>
        internal static async Task<ISet<string>> ClientesConPushDesde(NVEntities db, IReadOnlyCollection<string> clientes, DateTime desde)
        {
            List<string> lista = clientes.ToList();
            string marca = "\"tipo\":\"" + TIPO + "\"";
            List<string> conPush = await db.NotificacionesBuzon
                .Where(n => n.Aplicacion == Constantes.Aplicaciones.NESTO_TIENDAS && n.FechaCreacion >= desde
                    && n.Cliente != null && lista.Contains(n.Cliente) && n.Datos != null && n.Datos.Contains(marca))
                .Select(n => n.Cliente)
                .Distinct()
                .ToListAsync().ConfigureAwait(false);
            return new HashSet<string>(conPush.Select(c => c.Trim()), StringComparer.OrdinalIgnoreCase);
        }

        internal static MailMessage ConstruirCorreoSombra(ResultadoRecordatorioReposicionPushDTO resultado)
        {
            var mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión"),
                Subject = $"[Sombra] Push de reposición {resultado.Ahora:dd/MM/yyyy}: {resultado.Pushes.Count} clientes",
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(Constantes.Correos.INFORMATICA);

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 13px;'>");
            sb.AppendLine("<h2>Push de reposición en NestoTiendas (modo sombra, NestoAPI#662)</h2>");
            sb.AppendLine("<p><b>Esto es una prueba: no se ha enviado nada a ningún cliente.</b> Son los clientes a los que hoy les " +
                "habría llegado la push, con el texto exacto. Mismo cálculo que el recordatorio por correo (#532), solo para " +
                "clientes con la app y los consejos encendidos, sin grupo de control y como mucho una cada 7 días.</p>");
            if (resultado.Pushes.Any())
            {
                sb.AppendLine("<table border='1' cellpadding='5' cellspacing='0' style='border-collapse: collapse; font-size: 12px;'>");
                sb.AppendLine("<tr style='background-color: #4A90D9; color: white;'><th>Cliente</th><th>Nombre</th><th>Dispositivos</th>" +
                    "<th>Título</th><th>Texto</th><th>Productos (cantidad habitual)</th></tr>");
                foreach (PushReposicionDTO p in resultado.Pushes)
                {
                    sb.Append("<tr>");
                    sb.Append($"<td>{WebUtility.HtmlEncode(p.Cliente)}</td>");
                    sb.Append($"<td>{WebUtility.HtmlEncode(p.Nombre)}</td>");
                    sb.Append($"<td style='text-align: center;'>{p.Dispositivos}</td>");
                    sb.Append($"<td>{WebUtility.HtmlEncode(p.Notificacion.Titulo)}</td>");
                    sb.Append($"<td>{WebUtility.HtmlEncode(p.Notificacion.Cuerpo)}</td>");
                    sb.Append("<td>" + string.Join("<br/>", p.Productos.Select(x =>
                        $"{WebUtility.HtmlEncode(x.Producto)} {WebUtility.HtmlEncode(x.NombreProducto)} ({x.CantidadHabitual})")) + "</td>");
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</table>");
            }
            else
            {
                sb.AppendLine("<p>Hoy no le habría llegado a nadie.</p>");
            }
            if (resultado.ConPushReciente.Any())
            {
                sb.AppendLine($"<p>Con una push de reposición en los últimos {DIAS_ENTRE_PUSH} días (se quedan fuera): " +
                    $"{WebUtility.HtmlEncode(string.Join(", ", resultado.ConPushReciente))}</p>");
            }
            if (resultado.SinDispositivoConConsejos.Any())
            {
                sb.AppendLine("<p>Les tocaría, pero ya no tienen un dispositivo activo con los consejos encendidos: " +
                    $"{WebUtility.HtmlEncode(string.Join(", ", resultado.SinDispositivoConConsejos))}</p>");
            }
            int descartes = resultado.Calculo?.Descartes?.Count ?? 0;
            if (descartes > 0)
            {
                sb.AppendLine($"<p>Productos que tocarían por ritmo pero se quedan fuera (pedido pendiente, sustituto, sin stock...): {descartes}.</p>");
            }
            sb.AppendLine($"<p style='color: #888; font-size: 11px; margin-top: 20px;'>Generado el {DateTime.Now:dd/MM/yyyy HH:mm}. " +
                "Para encenderlo de verdad: parámetro RecordatorioReposicionPush de (defecto) a Activo; para apagarlo, a Apagado " +
                "(Scripts/Issue662_RecordatorioReposicionPush.sql).</p>");
            sb.AppendLine("</body></html>");
            mail.Body = sb.ToString();
            return mail;
        }
    }
}
