using Hangfire;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>
    /// NestoAPI#875: qué pasa con el enlace de la conexión online de una inscripción cuando cambia algo (se inscribe, pasa a
    /// online, se pone o cambia el enlace).
    /// </summary>
    public enum AccionEnlaceOnline
    {
        /// <summary>No le toca nada ahora (o le llegará con el recordatorio de siempre).</summary>
        Nada,
        /// <summary>Le toca ya el recordatorio con el enlace (y se marca su RecordatorioEnviado).</summary>
        Recordatorio,
        /// <summary>Ya tenía el recordatorio: un correo corto con el enlace nuevo.</summary>
        NuevoEnlace,
        /// <summary>Le tocaría el recordatorio, pero el evento todavía no tiene enlace: campana a quien mantiene el evento.</summary>
        FaltaEnlace
    }

    /// <summary>
    /// NestoAPI#875 (Novedades 1193 de Reina y 1251; Carlos 17/11/26): LA regla del enlace de la conexión online. Va en su propio
    /// campo (Eventos.EnlaceOnline); si está vacío, vale la dirección web que haya en «Lugar», como antes.
    /// </summary>
    public static class ReglasEnlaceOnlineEvento
    {
        public const int LONGITUD = 500;

        /// <summary>
        /// El enlace que viene en el cuerpo, comprobado: vacío → null (lo quita); si no, una dirección web (http:// o https://) de
        /// como mucho <see cref="LONGITUD"/> caracteres. 400 de usted si no.
        /// </summary>
        public static string Normalizar(string texto)
        {
            string limpio = texto?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            if (limpio.Length > LONGITUD)
            {
                throw new NestoBusinessException($"El enlace de la conexión online no puede tener más de {LONGITUD} caracteres.");
            }
            if (limpio.Any(char.IsWhiteSpace)
                || !(limpio.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || limpio.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                || !Uri.TryCreate(limpio, UriKind.Absolute, out _))
            {
                throw new NestoBusinessException("El enlace de la conexión online tiene que ser una dirección web completa, como https://teams.microsoft.com/…");
            }
            return limpio;
        }

        /// <summary>El enlace del evento: el de su campo y, si está vacío, la dirección web de «Lugar» (compatibilidad). Null si no hay.</summary>
        public static string DelEvento(string enlaceOnline, string lugar)
        {
            string propio = enlaceOnline?.Trim();
            return string.IsNullOrEmpty(propio) ? FormatosEvento.EnlaceDelLugar(lugar) : propio;
        }

        /// <summary>
        /// El enlace que recibe una inscripción (<see cref="FormatosEvento.LlevaEnlace"/>: online todas, mixto solo las online), o null.
        /// </summary>
        public static string ParaInscripcion(Evento evento, string enlaceOnline, string formaAsistencia) =>
            FormatosEvento.LlevaEnlace(evento, formaAsistencia) ? DelEvento(enlaceOnline, evento?.Lugar) : null;

        /// <summary>¿Ha cambiado el enlace que se manda? (el de antes y el de ahora, ya resueltos con <see cref="DelEvento"/>).</summary>
        public static bool HaCambiado(string antes, string despues) =>
            !string.Equals(antes?.Trim() ?? string.Empty, despues?.Trim() ?? string.Empty, StringComparison.Ordinal);

        /// <summary>La inscripción sigue esperando al evento: pagada, facturada o inscrita (gratuita) y sin «No podré ir».</summary>
        public static bool EsperaAlEvento(EventoInscripcion inscripcion)
        {
            EstadoInscripcionEvento? estado = ReglasInscripcionEvento.Estado(inscripcion?.Estado);
            return estado.HasValue && ReglasInscripcionEvento.ESPERAN_AL_EVENTO.Contains(estado.Value)
                && ReglasInscripcionEvento.OcupaPlaza(inscripcion);
        }

        /// <summary>
        /// El evento ya ha acabado (o, sin hora de fin, ya ha empezado; sin horario, cuando acaba el día): a partir de ahí no se manda
        /// nada. <paramref name="ahora"/> en hora de Madrid, como la fecha y las horas del evento.
        /// </summary>
        public static bool YaHaPasado(Evento evento, DateTime ahora)
        {
            if (evento == null)
            {
                return true;
            }
            DateTime dia = evento.Fecha.Date;
            if (dia != ahora.Date)
            {
                return dia < ahora.Date;
            }
            TimeSpan? limite = evento.HoraFin ?? evento.HoraInicio;
            return limite.HasValue && ahora.TimeOfDay >= limite.Value;
        }

        /// <summary>
        /// Al inscribirse online (o pasar a online, o quedar pagada): si su recordatorio ya habría salido (el evento es dentro de
        /// <see cref="RecordatorioEventosJobsService.DIAS_ANTES"/> días o menos), se le manda ya; si el evento no tiene enlace,
        /// campana a quien lo mantiene. Si el evento es más adelante, nada: el recordatorio de siempre le llegará con el enlace.
        /// </summary>
        public static AccionEnlaceOnline AlInscribirse(Evento evento, EventoInscripcion inscripcion, string formaAsistencia,
            string enlaceOnline, DateTime ahora)
        {
            if (evento == null || !evento.Activo || inscripcion == null || inscripcion.RecordatorioEnviado.HasValue
                || !EsperaAlEvento(inscripcion) || !FormatosEvento.LlevaEnlace(evento, formaAsistencia) || YaHaPasado(evento, ahora))
            {
                return AccionEnlaceOnline.Nada;
            }
            if ((evento.Fecha.Date - ahora.Date).Days > RecordatorioEventosJobsService.DIAS_ANTES)
            {
                return AccionEnlaceOnline.Nada;
            }
            return ParaInscripcion(evento, enlaceOnline, formaAsistencia) == null ? AccionEnlaceOnline.FaltaEnlace : AccionEnlaceOnline.Recordatorio;
        }

        /// <summary>
        /// Al poner o cambiar el enlace (Carlos 17/11/26, decisión 4 de la issue): a las inscritas online que ya tenían el
        /// recordatorio, un correo corto con el enlace nuevo; a las que lo estaban esperando (el recordatorio ya les tocaba pero no
        /// había enlace), el recordatorio entero. Las demás, nada.
        /// </summary>
        public static AccionEnlaceOnline AlCambiarEnlace(Evento evento, EventoInscripcion inscripcion, string formaAsistencia,
            string enlaceOnline, DateTime ahora)
        {
            if (evento == null || !evento.Activo || inscripcion == null || !EsperaAlEvento(inscripcion)
                || !FormatosEvento.LlevaEnlace(evento, formaAsistencia) || YaHaPasado(evento, ahora)
                || ParaInscripcion(evento, enlaceOnline, formaAsistencia) == null)
            {
                return AccionEnlaceOnline.Nada;
            }
            if (inscripcion.RecordatorioEnviado.HasValue)
            {
                return AccionEnlaceOnline.NuevoEnlace;
            }
            AccionEnlaceOnline alInscribirse = AlInscribirse(evento, inscripcion, formaAsistencia, enlaceOnline, ahora);
            return alInscribirse == AccionEnlaceOnline.Recordatorio ? AccionEnlaceOnline.Recordatorio : AccionEnlaceOnline.Nada;
        }
    }

    /// <summary>NestoAPI#875: los textos (de usted) del correo «nuevo enlace» y de la campana «falta el enlace».</summary>
    public static class TextosEnlaceOnlineEvento
    {
        public const string TIPO_FALTA_ENLACE = "EventoSinEnlaceOnline";
        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        public static string AsuntoNuevoEnlace(Evento evento, bool habiaEnlace) =>
            $"{(habiaEnlace ? "Nuevo enlace" : "Enlace")} para conectarse a «{evento?.Titulo?.Trim()}»";

        /// <summary>El correo corto con el enlace (mismo remitente y estilo que el recordatorio).</summary>
        public static MailMessage CorreoNuevoEnlace(Evento evento, string enlace, string nombre, string destino, bool habiaEnlace)
        {
            string Html(string t) => WebUtility.HtmlEncode(t ?? string.Empty);
            var cuerpo = new StringBuilder();
            cuerpo.Append("<!DOCTYPE html><html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 14px; max-width: 600px;'>");
            cuerpo.Append($"<p>Hola{(string.IsNullOrWhiteSpace(nombre) ? "" : ", " + Html(nombre.Trim()))}:</p>");
            cuerpo.Append(habiaEnlace
                ? $"<p>Ha cambiado el enlace para conectarse a <b>«{Html(evento.Titulo?.Trim())}»</b>, {Html(RecordatorioEventosJobsService.CuandoYDonde(evento, FormatosEvento.ONLINE))}. Use este, por favor:</p>"
                : $"<p>Este es el enlace para conectarse a <b>«{Html(evento.Titulo?.Trim())}»</b>, {Html(RecordatorioEventosJobsService.CuandoYDonde(evento, FormatosEvento.ONLINE))}:</p>");
            cuerpo.Append($"<p><a href='{Html(enlace)}'>{Html(enlace)}</a></p>");
            if (habiaEnlace)
            {
                cuerpo.Append("<p>El enlace anterior ya no vale.</p>");
            }
            cuerpo.Append("<p>Muchas gracias.</p><p>Nueva Visión</p></body></html>");
            var mail = new MailMessage
            {
                From = new MailAddress("nuevavision@nuevavision.es", "Nueva Visión"),
                Subject = AsuntoNuevoEnlace(evento, habiaEnlace),
                Body = cuerpo.ToString(),
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(destino);
            return mail;
        }

        /// <summary>La campana a quien mantiene el evento: hay inscritas online esperando el enlace y el evento no lo tiene.</summary>
        public static NotificacionPushDTO NotificacionFaltaEnlace(Evento evento, int esperando) => new NotificacionPushDTO
        {
            Titulo = $"Falta el enlace de «{evento?.Titulo?.Trim()}»",
            Cuerpo = $"«{evento?.Titulo?.Trim()}» es el {evento?.Fecha.ToString("dddd d 'de' MMMM", es)} y " +
                (esperando == 1 ? "hay una inscripción online esperando" : $"hay {esperando} inscripciones online esperando") +
                " el enlace de la conexión, pero el evento no lo tiene. Póngalo en «Enlace de la conexión online» de la ficha del evento " +
                "en Nesto: en cuanto lo guarde, les llegará.",
            Tipo = TIPO_FALTA_ENLACE,
            Datos = new Dictionary<string, string>
            {
                ["tipo"] = TIPO_FALTA_ENLACE,
                ["eventoId"] = evento?.Id.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            }
        };
    }

    /// <summary>
    /// NestoAPI#875: la columna Eventos.EnlaceOnline (Scripts/Issue875_EventosEnlaceOnline.sql), fuera del EDMX. Sin la columna:
    /// ningún evento la tiene (vale «Lugar», como antes) y guardar contesta false.
    /// </summary>
    public interface IRepositorioEnlaceOnlineEventos
    {
        /// <summary>Evento → su enlace (solo los que lo tienen).</summary>
        Task<Dictionary<int, string>> Leer(IReadOnlyCollection<int> eventos);
        /// <summary>Guarda el enlace (null lo quita). False si la columna todavía no existe.</summary>
        Task<bool> Guardar(int evento, string enlace);
    }

    public class RepositorioEnlaceOnlineEventos : IRepositorioEnlaceOnlineEventos
    {
        internal const string SQL_EXISTE = "SELECT CAST(CASE WHEN COL_LENGTH('dbo.Eventos', 'EnlaceOnline') IS NULL THEN 0 ELSE 1 END AS bit)";
        internal const string SQL_LEER = "SELECT Id, EnlaceOnline FROM dbo.Eventos WITH (NOLOCK) WHERE EnlaceOnline IS NOT NULL AND Id IN ({0})";
        internal const string SQL_GUARDAR = "UPDATE dbo.Eventos SET EnlaceOnline = @enlace WHERE Id = @id";

        private static readonly ComprobadorEsquemaEventos columna = new ComprobadorEsquemaEventos(SQL_EXISTE);
        private static int falloRegistrado;
        private readonly NVEntities db;

        public RepositorioEnlaceOnlineEventos(NVEntities db)
        {
            this.db = db;
        }

        public async Task<Dictionary<int, string>> Leer(IReadOnlyCollection<int> eventos)
        {
            List<int> ids = (eventos ?? new List<int>()).Distinct().ToList();
            var resultado = new Dictionary<int, string>();
            if (!ids.Any() || !columna.Existe(db))
            {
                return resultado;
            }
            foreach (List<int> tanda in RepositorioPersonaAsistenteEventos.Tandas(ids, 500))
            {
                foreach (FilaEnlace fila in await db.Database
                    .SqlQuery<FilaEnlace>(string.Format(CultureInfo.InvariantCulture, SQL_LEER, string.Join(",", tanda)))
                    .ToListAsync().ConfigureAwait(false))
                {
                    string enlace = fila.EnlaceOnline?.Trim();
                    if (!string.IsNullOrEmpty(enlace))
                    {
                        resultado[fila.Id] = enlace;
                    }
                }
            }
            return resultado;
        }

        public async Task<bool> Guardar(int evento, string enlace)
        {
            if (!columna.Existe(db))
            {
                return false;
            }
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_GUARDAR,
                new SqlParameter("@enlace", SqlDbType.NVarChar, ReglasEnlaceOnlineEvento.LONGITUD) { Value = (object)enlace ?? DBNull.Value },
                new SqlParameter("@id", SqlDbType.Int) { Value = evento }).ConfigureAwait(false);
            return true;
        }

        /// <summary>Para las listas y los jobs: sin repositorio, sin la columna o si algo falla, ninguno lo tiene (ELMAH una vez).</summary>
        internal static async Task<Dictionary<int, string>> LeerSinFallar(IRepositorioEnlaceOnlineEventos repositorio, IEnumerable<int> eventos)
        {
            List<int> ids = (eventos ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (repositorio == null || !ids.Any())
            {
                return new Dictionary<int, string>();
            }
            try
            {
                return await repositorio.Leer(ids).ConfigureAwait(false) ?? new Dictionary<int, string>();
            }
            catch (Exception ex)
            {
                if (System.Threading.Interlocked.Exchange(ref falloRegistrado, 1) == 0)
                {
                    ElmahHelper.Log(new Exception($"[Eventos #875] No se ha podido leer el enlace de la conexión online de los eventos (vale el de «Lugar»): {ex.Message}", ex));
                }
                return new Dictionary<int, string>();
            }
        }

        internal static async Task<string> LeerUnoSinFallar(IRepositorioEnlaceOnlineEventos repositorio, int evento) =>
            (await LeerSinFallar(repositorio, new[] { evento }).ConfigureAwait(false)).TryGetValue(evento, out string enlace) ? enlace : null;

        private class FilaEnlace
        {
            public int Id { get; set; }
            public string EnlaceOnline { get; set; }
        }
    }

    /// <summary>
    /// NestoAPI#875: a quién va la campana de un evento (quien lo mantiene): los del grupo Tiendas cuyo AlmacénPedidoVta es la tienda
    /// del evento; si no se encuentra a nadie, Dirección. Usuarios con dominio, para el buzón de Nesto.
    /// </summary>
    internal static class DestinatariosEvento
    {
        private const string DOMINIO = "NUEVAVISION\\";

        internal static async Task<List<string>> QuienMantiene(NVEntities db, Evento evento)
        {
            string empresa = evento?.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string almacen = evento?.Almacen?.Trim();
            List<string> deLaTienda = new List<string>();
            if (!string.IsNullOrEmpty(almacen))
            {
                List<string> miembros = MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.TIENDAS) ?? new List<string>();
                string clave = PermisosEventos.CLAVE_ALMACEN_USUARIO;
                List<string> conEsaTienda = (await db.ParametrosUsuario
                    .Where(p => p.Empresa == empresa && p.Clave == clave)
                    .Select(p => new { p.Usuario, p.Valor }).ToListAsync().ConfigureAwait(false))
                    .Where(p => string.Equals(p.Valor?.Trim(), almacen, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Usuario?.Trim()).ToList();
                deLaTienda = Cruzar(miembros, conEsaTienda);
            }
            if (!deLaTienda.Any())
            {
                deLaTienda = (MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.DIRECCION) ?? new List<string>())
                    .Select(PermisosEventos.SinDominio).Where(u => u != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            return deLaTienda.Select(u => DOMINIO + u).ToList();
        }

        /// <summary>Los miembros del grupo que tienen esa tienda (sin dominio, sin repetir).</summary>
        internal static List<string> Cruzar(IEnumerable<string> miembrosGrupo, IEnumerable<string> usuariosDeLaTienda)
        {
            var tienda = new HashSet<string>((usuariosDeLaTienda ?? Enumerable.Empty<string>()).Select(PermisosEventos.SinDominio).Where(u => u != null),
                StringComparer.OrdinalIgnoreCase);
            return (miembrosGrupo ?? Enumerable.Empty<string>()).Select(PermisosEventos.SinDominio)
                .Where(u => u != null && tienda.Contains(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Campana en el buzón de Nesto de quien mantiene el evento. Nunca lanza.</summary>
        internal static async Task Avisar(NVEntities db, Evento evento, NotificacionPushDTO notificacion, IServicioNotificacionesPush push)
        {
            try
            {
                foreach (string usuario in await QuienMantiene(db, evento).ConfigureAwait(false))
                {
                    await push.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Eventos] No se ha podido avisar a quien mantiene el evento {evento?.Id}: {ex.Message}", ex));
            }
        }
    }

    /// <summary>Dependencias de los envíos del enlace (inyectables en los tests).</summary>
    internal class DependenciasEnlaceOnlineEventos
    {
        public Func<DateTime> Ahora { get; set; }
        /// <summary>El interruptor del recordatorio (Eventos:RecordatorioAsistencia): apagado, no se manda el recordatorio a destiempo.</summary>
        public Func<bool> RecordatorioEncendido { get; set; }
        /// <summary>Para mandar el recordatorio (Push, Correo, CorreoCliente, NombreCliente) con el mismo núcleo que el job.</summary>
        public DependenciasRecordatorioEventos Recordatorio { get; set; }
        /// <summary>(evento) → las inscripciones del evento con su forma de asistencia.</summary>
        public Func<int, Task<List<KeyValuePair<EventoInscripcion, string>>>> Inscripciones { get; set; }
        /// <summary>Pone la clave de asistencia a las que no la tienen (para los botones del correo).</summary>
        public Func<IEnumerable<EventoInscripcion>, Task> PonerClaves { get; set; }
        /// <summary>Marca RecordatorioEnviado.</summary>
        public Func<IEnumerable<EventoInscripcion>, Task> MarcarEnviados { get; set; }
        /// <summary>(evento, notificación) → campana a quien lo mantiene.</summary>
        public Func<Evento, NotificacionPushDTO, Task> AvisarTienda { get; set; }
    }

    /// <summary>Lo que ha hecho una pasada (tests y diagnóstico).</summary>
    public class ResultadoEnlaceOnlineEventos
    {
        public int Recordatorios { get; set; }
        public int NuevosEnlaces { get; set; }
        public int EsperandoEnlace { get; set; }
    }

    /// <summary>
    /// NestoAPI#875: los envíos del enlace fuera del recordatorio de las 10:00. En segundo plano (Hangfire), para no frenar el guardado:
    /// <list type="bullet">
    /// <item><see cref="EnlaceCambiado"/>: se ha puesto o cambiado el enlace del evento.</item>
    /// <item><see cref="AlInscribirse"/>: una inscripción online nueva (o que pasa a online o queda pagada) con el recordatorio ya en
    /// marcha (lo llama el job de avisos de la inscripción, <see cref="AvisosInscripcionEventosJobs"/>).</item>
    /// </list>
    /// </summary>
    public static class EnlaceOnlineEventosJobs
    {
        /// <summary>Encola el aviso del enlace cambiado. Nunca lanza.</summary>
        public static void Encolar(int eventoId, bool habiaEnlace)
        {
            try
            {
                _ = BackgroundJob.Enqueue(() => EnlaceCambiado(eventoId, habiaEnlace));
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Eventos #875] No se ha podido encolar el aviso del enlace nuevo del evento {eventoId}: {ex.Message}", ex));
            }
        }

        [AutomaticRetry(Attempts = 2)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static async Task EnlaceCambiado(int eventoId, bool habiaEnlace)
        {
            using (NVEntities db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                Evento evento = await db.Eventos.SingleOrDefaultAsync(e => e.Id == eventoId).ConfigureAwait(false);
                if (evento == null)
                {
                    return;
                }
                string enlace = await RepositorioEnlaceOnlineEventos.LeerUnoSinFallar(new RepositorioEnlaceOnlineEventos(db), eventoId).ConfigureAwait(false);
                _ = await EnlaceCambiado(Dependencias(db), evento, enlace, habiaEnlace).ConfigureAwait(false);
            }
        }

        internal static DependenciasEnlaceOnlineEventos Dependencias(NVEntities db)
        {
            var servicio = new ServicioInscripcionesEventos(db);
            var push = new ServicioNotificacionesPush();
            return new DependenciasEnlaceOnlineEventos
            {
                Ahora = () => DateTime.Now,
                RecordatorioEncendido = () => InterruptoresEventos.RecordatorioAsistencia,
                Recordatorio = new DependenciasRecordatorioEventos
                {
                    CorreoCliente = (empresa, cliente, contacto) => RecordatorioEventosJobsService.CorreoDelCliente(db, empresa, cliente, contacto),
                    NombreCliente = (empresa, cliente, contacto) => RecordatorioEventosJobsService.NombreDelCliente(db, empresa, cliente, contacto),
                    Push = push,
                    Correo = new ServicioCorreoElectronico()
                },
                Inscripciones = id => InscripcionesConForma(db, id),
                PonerClaves = async inscripciones =>
                {
                    foreach (EventoInscripcion inscripcion in inscripciones.Where(i => i.ClaveAsistencia == null))
                    {
                        inscripcion.ClaveAsistencia = Guid.NewGuid();
                    }
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                },
                MarcarEnviados = inscripciones => servicio.MarcarRecordatorioEnviado(inscripciones),
                AvisarTienda = (evento, notificacion) => DestinatariosEvento.Avisar(db, evento, notificacion, push)
            };
        }

        /// <summary>Las inscripciones del evento que esperan al evento, con su forma de asistencia (solo cuenta en los mixtos).</summary>
        internal static async Task<List<KeyValuePair<EventoInscripcion, string>>> InscripcionesConForma(NVEntities db, int eventoId)
        {
            List<string> estados = ReglasInscripcionEvento.ESPERAN_AL_EVENTO.Select(e => e.ToString()).ToList();
            List<EventoInscripcion> inscripciones = await db.EventosInscripciones
                .Where(i => i.EventoId == eventoId && estados.Contains(i.Estado)).ToListAsync().ConfigureAwait(false);
            Dictionary<int, string> formas = await RepositorioFormaAsistenciaEventos.LeerSinFallar(new RepositorioFormaAsistenciaEventos(db),
                inscripciones.Select(i => i.Id)).ConfigureAwait(false);
            return inscripciones.Select(i => new KeyValuePair<EventoInscripcion, string>(i, formas.TryGetValue(i.Id, out string f) ? f : null)).ToList();
        }

        /// <summary>Núcleo de «se ha puesto o cambiado el enlace».</summary>
        internal static async Task<ResultadoEnlaceOnlineEventos> EnlaceCambiado(DependenciasEnlaceOnlineEventos deps, Evento evento,
            string enlaceOnline, bool habiaEnlace)
        {
            var resultado = new ResultadoEnlaceOnlineEventos();
            DateTime ahora = deps.Ahora();
            List<KeyValuePair<EventoInscripcion, string>> inscripciones = await deps.Inscripciones(evento.Id).ConfigureAwait(false)
                ?? new List<KeyValuePair<EventoInscripcion, string>>();
            var recordatorios = new List<KeyValuePair<EventoInscripcion, string>>();
            foreach (KeyValuePair<EventoInscripcion, string> fila in inscripciones)
            {
                switch (ReglasEnlaceOnlineEvento.AlCambiarEnlace(evento, fila.Key, fila.Value, enlaceOnline, ahora))
                {
                    case AccionEnlaceOnline.NuevoEnlace:
                        if (await MandarNuevoEnlace(deps, evento, fila.Key, ReglasEnlaceOnlineEvento.ParaInscripcion(evento, enlaceOnline, fila.Value), habiaEnlace).ConfigureAwait(false))
                        {
                            resultado.NuevosEnlaces++;
                        }
                        break;
                    case AccionEnlaceOnline.Recordatorio:
                        recordatorios.Add(fila);
                        break;
                }
            }
            if (recordatorios.Any() && deps.RecordatorioEncendido())
            {
                resultado.Recordatorios = await MandarRecordatorios(deps, evento, recordatorios, enlaceOnline).ConfigureAwait(false);
            }
            return resultado;
        }

        /// <summary>
        /// Núcleo de «se ha inscrito (o ha pasado a online, o ha quedado pagada)»: le manda el recordatorio con el enlace si ya le
        /// tocaba, o avisa a quien mantiene el evento si falta el enlace.
        /// </summary>
        internal static async Task<AccionEnlaceOnline> AlInscribirse(DependenciasEnlaceOnlineEventos deps, Evento evento,
            EventoInscripcion inscripcion, string formaAsistencia, string enlaceOnline)
        {
            AccionEnlaceOnline accion = ReglasEnlaceOnlineEvento.AlInscribirse(evento, inscripcion, formaAsistencia, enlaceOnline, deps.Ahora());
            if (!deps.RecordatorioEncendido())
            {
                return AccionEnlaceOnline.Nada;
            }
            if (accion == AccionEnlaceOnline.Recordatorio)
            {
                _ = await MandarRecordatorios(deps, evento, new List<KeyValuePair<EventoInscripcion, string>>
                {
                    new KeyValuePair<EventoInscripcion, string>(inscripcion, formaAsistencia)
                }, enlaceOnline).ConfigureAwait(false);
            }
            else if (accion == AccionEnlaceOnline.FaltaEnlace)
            {
                await deps.AvisarTienda(evento, TextosEnlaceOnlineEvento.NotificacionFaltaEnlace(evento, 1)).ConfigureAwait(false);
            }
            return accion;
        }

        private static async Task<int> MandarRecordatorios(DependenciasEnlaceOnlineEventos deps, Evento evento,
            List<KeyValuePair<EventoInscripcion, string>> filas, string enlaceOnline)
        {
            await deps.PonerClaves(filas.Select(f => f.Key).ToList()).ConfigureAwait(false);
            var enviadas = new List<EventoInscripcion>();
            foreach (KeyValuePair<EventoInscripcion, string> fila in filas)
            {
                try
                {
                    _ = await RecordatorioEventosJobsService.Avisar(deps.Recordatorio, evento, fila.Key, fila.Value, enlaceOnline).ConfigureAwait(false);
                    enviadas.Add(fila.Key);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Eventos #875] No se ha podido mandar el recordatorio con el enlace a la inscripción {fila.Key.Id}: {ex.Message}", ex));
                }
            }
            if (enviadas.Any())
            {
                await deps.MarcarEnviados(enviadas).ConfigureAwait(false);
            }
            return enviadas.Count;
        }

        private static async Task<bool> MandarNuevoEnlace(DependenciasEnlaceOnlineEventos deps, Evento evento, EventoInscripcion inscripcion,
            string enlace, bool habiaEnlace)
        {
            try
            {
                string empresa = inscripcion.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
                string cliente = inscripcion.Cliente?.Trim();
                string contacto = inscripcion.Contacto?.Trim();
                string destino = await deps.Recordatorio.CorreoCliente(empresa, cliente, contacto).ConfigureAwait(false);
                if (destino == null)
                {
                    return false;
                }
                string nombre = await deps.Recordatorio.NombreCliente(empresa, cliente, contacto).ConfigureAwait(false);
                using (MailMessage mail = TextosEnlaceOnlineEvento.CorreoNuevoEnlace(evento, enlace, nombre, destino, habiaEnlace))
                {
                    if (deps.Recordatorio.Correo.EnviarCorreoSMTP(mail))
                    {
                        return true;
                    }
                }
                ElmahHelper.Log(new Exception($"[Eventos #875] No se ha podido mandar el enlace nuevo a {destino} (inscripción {inscripcion.Id})."));
                return false;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Eventos #875] No se ha podido mandar el enlace nuevo a la inscripción {inscripcion.Id}: {ex.Message}", ex));
                return false;
            }
        }
    }
}
