using Hangfire;
using NestoAPI.Infraestructure.Novedades;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>Sugerencia 1332 (Reina; Carlos 18/11/26): el dosier (PDF) del evento, sin el contenido.</summary>
    public class DosierEventoDTO
    {
        public int EventoId { get; set; }
        public string Nombre { get; set; }
        /// <summary>Bytes.</summary>
        public int Tamano { get; set; }
        /// <summary>Cuándo se subió (o se sustituyó).</summary>
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
        /// <summary>Solo lectura: cuándo sale solo (2 horas antes del inicio, hora de Madrid); null si el evento ya pasó.</summary>
        public DateTime? EnvioPrevisto { get; set; }
        /// <summary>Solo lectura: a cuántas inscripciones se les ha mandado ya.</summary>
        public int Enviados { get; set; }
    }

    /// <summary>El dosier ya validado, listo para grabar (o leído, con el contenido).</summary>
    public class DosierEvento
    {
        public int EventoId { get; set; }
        public string Nombre { get; set; }
        public byte[] Contenido { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
    }

    /// <summary>
    /// Sugerencia 1332 (Reina, Carlos 18/11/26: «si hay que mandar un dossier lo que hay que hacer es automatizar el envío»): LAS
    /// reglas del dosier del evento. Un PDF por evento que se manda solo, por correo, a todas las inscritas vivas unas
    /// <see cref="HORAS_ANTES_DOSIER"/> horas antes de empezar, una sola vez cada una.
    /// </summary>
    public static class ReglasDosierEvento
    {
        public const int HORAS_ANTES_DOSIER = 2;
        public const int TAMANO_MAXIMO = 10 * 1024 * 1024;
        public const string TIPO_PDF = "application/pdf";

        public class Validacion
        {
            public DosierEvento Dosier { get; set; }
            public string Error { get; set; }
            /// <summary>415 en vez de 400.</summary>
            public bool TipoNoPermitido { get; set; }
        }

        /// <summary>Un solo fichero, PDF de verdad (empieza por %PDF-), no vacío y de como mucho 10 MB. Errores de usted.</summary>
        public static Validacion Validar(IEnumerable<(string Nombre, string Tipo, byte[] Contenido)> ficheros)
        {
            List<(string Nombre, string Tipo, byte[] Contenido)> lista = (ficheros ?? Enumerable.Empty<(string, string, byte[])>()).ToList();
            if (lista.Count == 0)
            {
                return new Validacion { Error = $"No ha llegado ningún fichero. Envíelo como multipart/form-data en el campo «{ReglasAdjuntosNovedades.CAMPO_FICHERO}»." };
            }
            if (lista.Count > 1)
            {
                return new Validacion { Error = "Un evento lleva un solo dosier: mande solo un fichero." };
            }
            (string nombreOriginal, string tipoOriginal, byte[] contenido) = lista[0];
            string nombre = ReglasAdjuntosNovedades.NormalizarNombre(nombreOriginal);
            if (string.IsNullOrEmpty(nombre))
            {
                return new Validacion { Error = "Falta el nombre del fichero." };
            }
            string tipo = ReglasAdjuntosNovedades.NormalizarTipo(tipoOriginal);
            bool tipoAceptable = tipo == null || tipo == TIPO_PDF || tipo == "application/octet-stream";
            if (!string.Equals(Path.GetExtension(nombre), ".pdf", StringComparison.OrdinalIgnoreCase) || !tipoAceptable)
            {
                return new Validacion { Error = $"«{nombre}» no es un PDF: el dosier del evento tiene que ser un PDF.", TipoNoPermitido = true };
            }
            if (contenido == null || contenido.Length == 0)
            {
                return new Validacion { Error = $"«{nombre}» está vacío." };
            }
            if (contenido.Length > TAMANO_MAXIMO)
            {
                return new Validacion { Error = $"«{nombre}» pesa demasiado ({contenido.Length / (1024.0 * 1024):0.#} MB; el máximo es {TAMANO_MAXIMO / (1024 * 1024)} MB)." };
            }
            if (!EsPdf(contenido))
            {
                return new Validacion { Error = $"«{nombre}» no es un PDF válido (se llama .pdf, pero por dentro es otra cosa).", TipoNoPermitido = true };
            }
            return new Validacion { Dosier = new DosierEvento { Nombre = nombre, Contenido = contenido } };
        }

        /// <summary>Los PDF empiezan por «%PDF-» (se admite algo de basura delante, como hacen los lectores: hasta 1 KB).</summary>
        public static bool EsPdf(byte[] contenido)
        {
            if (contenido == null || contenido.Length < 5)
            {
                return false;
            }
            byte[] firma = Encoding.ASCII.GetBytes("%PDF-");
            int hasta = Math.Min(1024, contenido.Length - firma.Length);
            for (int i = 0; i <= hasta; i++)
            {
                bool casa = true;
                for (int j = 0; j < firma.Length && casa; j++)
                {
                    casa = contenido[i + j] == firma[j];
                }
                if (casa)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Cuándo sale el dosier (hora de Madrid): <see cref="HORAS_ANTES_DOSIER"/> horas antes de la hora de inicio, sin salirse del
        /// día del evento (un evento a las 8:00 lo recibe desde las 0:00, y el job lo manda en su primera pasada, a las 7:00).
        /// Sin hora de inicio, null: no sale (Carlos 18/11/26: no puede haber eventos sin hora; los antiguos sin ella no lo reciben).
        /// </summary>
        public static DateTime? MomentoEnvio(Evento evento)
        {
            if (evento?.HoraInicio == null)
            {
                return null;
            }
            DateTime dia = evento.Fecha.Date;
            DateTime momento = dia + evento.HoraInicio.Value - TimeSpan.FromHours(HORAS_ANTES_DOSIER);
            return momento < dia ? dia : momento;
        }

        /// <summary>Hasta cuándo se manda: hasta que acaba el evento (su hora de fin; sin ella, hasta que acaba el día).</summary>
        public static DateTime FinEnvio(Evento evento)
        {
            DateTime dia = evento.Fecha.Date;
            return evento.HoraFin.HasValue ? dia + evento.HoraFin.Value : dia.AddDays(1);
        }

        /// <summary>
        /// ¿Toca mandarlo ya? Evento activo, con hora de inicio, ya es la hora (<see cref="MomentoEnvio"/>) y no ha acabado
        /// (<see cref="FinEnvio"/>). Gratuito o de pago, da igual. Si el evento cambia de hora, vale la nueva (se mira en cada pasada).
        /// </summary>
        public static bool TocaEnviar(Evento evento, DateTime ahoraMadrid)
        {
            DateTime? momento = MomentoEnvio(evento);
            return momento.HasValue && evento.Activo && ahoraMadrid >= momento.Value && ahoraMadrid < FinEnvio(evento);
        }

        /// <summary>
        /// ¿Lo recibe esta inscripción? Solo las formalizadas (<see cref="ReglasInscripcionEvento.EstaFormalizada"/>: pagadas,
        /// facturadas o inscritas en un evento gratuito, sin «No podré ir») y las que ya tienen pasado «Asistió»; y solo si todavía
        /// no lo tiene. Carlos (18/11/26): una reserva sin pagar no está formalizada; si paga a última hora, lo recibe al pagar,
        /// exactamente igual que quien se inscribe a última hora.
        /// </summary>
        public static bool LoRecibe(EventoInscripcion inscripcion, bool yaEnviado)
        {
            if (inscripcion == null || yaEnviado)
            {
                return false;
            }
            return ReglasInscripcionEvento.EstaFormalizada(inscripcion)
                || ReglasInscripcionEvento.Estado(inscripcion.Estado) == EstadoInscripcionEvento.Asistio;
        }

        /// <summary>
        /// Al sustituir el dosier pidiendo reenviarlo (Carlos 18/11/26): las inscripciones que ya tenían el anterior y lo seguirían
        /// recibiendo (<see cref="LoRecibe"/> sin contar la marca). Solo mientras <see cref="TocaEnviar"/>: antes nadie lo tiene y,
        /// acabado el evento, ya no se manda nada.
        /// </summary>
        public static List<int> AReenviar(Evento evento, IEnumerable<EventoInscripcion> inscripciones, ISet<int> enviados, DateTime ahoraMadrid)
        {
            if (!TocaEnviar(evento, ahoraMadrid) || inscripciones == null || enviados == null)
            {
                return new List<int>();
            }
            return inscripciones.Where(i => i != null && enviados.Contains(i.Id) && LoRecibe(i, false)).Select(i => i.Id).Distinct().ToList();
        }

        /// <summary>«que empieza hoy a las 17:00», «que ha empezado hoy a las 17:00» o «que es hoy» (sin hora).</summary>
        public static string CuandoEmpieza(Evento evento, DateTime ahoraMadrid)
        {
            string hora = ReglasModalidadEvento.EscribirHora(evento.HoraInicio);
            if (hora == null)
            {
                return "que es hoy";
            }
            bool empezado = ahoraMadrid >= evento.Fecha.Date + evento.HoraInicio.Value;
            return $"que {(empezado ? "ha empezado" : "empieza")} hoy a las {hora}";
        }

        /// <summary>La hora de Madrid (la del evento) a partir de la hora UTC.</summary>
        public static DateTime AhoraMadrid(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Startup.ZonaHorariaPeninsular());

        /// <summary>
        /// El correo (de usted) con el PDF adjunto, mismo remitente y estilo que el recordatorio. <paramref name="actualizado"/>: es la
        /// versión nueva para quien ya tenía la anterior.
        /// </summary>
        public static MailMessage Correo(Evento evento, DosierEvento dosier, string nombre, string destino, DateTime ahoraMadrid, bool actualizado = false)
        {
            string Html(string t) => WebUtility.HtmlEncode(t ?? string.Empty);
            var cuerpo = new StringBuilder();
            cuerpo.Append("<!DOCTYPE html><html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 14px; max-width: 600px;'>");
            cuerpo.Append($"<p>Hola{(string.IsNullOrWhiteSpace(nombre) ? "" : ", " + Html(nombre.Trim()))}:</p>");
            if (actualizado)
            {
                cuerpo.Append($"<p>Le enviamos la versión actualizada del dosier de <b>«{Html(evento.Titulo?.Trim())}»</b>, {Html(CuandoEmpieza(evento, ahoraMadrid))}. " +
                    "Sustituye al que le mandamos antes: use este. Lo tiene adjunto a este correo, en PDF.</p>");
            }
            else
            {
                cuerpo.Append($"<p>Le enviamos el dosier de <b>«{Html(evento.Titulo?.Trim())}»</b>, {Html(CuandoEmpieza(evento, ahoraMadrid))}. Lo tiene adjunto a este correo, en PDF.</p>");
            }
            cuerpo.Append("<p>Esperamos que lo disfrute.</p><p>Nueva Visión</p></body></html>");
            var mail = new MailMessage
            {
                From = new MailAddress("nuevavision@nuevavision.es", "Nueva Visión"),
                Subject = actualizado ? $"Dosier actualizado de «{evento.Titulo?.Trim()}»" : $"Dosier de «{evento.Titulo?.Trim()}»",
                Body = cuerpo.ToString(),
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(destino);
            mail.Attachments.Add(new Attachment(new MemoryStream(dosier.Contenido), dosier.Nombre, TIPO_PDF));
            return mail;
        }
    }

    /// <summary>
    /// Sugerencia 1332: dbo.EventosDosier (un PDF por evento) y la marca por inscripción EventosInscripciones.DosierEnviado
    /// (Scripts/EventosDosier.sql), fuera del EDMX, como las imágenes de los eventos (#690). Sin el script: los endpoints dan 503
    /// y el envío no hace nada.
    /// </summary>
    public interface IRepositorioDosierEventos
    {
        /// <summary>¿Están la tabla y la columna de la marca?</summary>
        bool Disponible();
        /// <summary>El dosier de cada evento, sin el contenido, con cuántas inscripciones lo tienen ya.</summary>
        Task<Dictionary<int, DosierEventoDTO>> LeerInfo(IReadOnlyCollection<int> eventos);
        /// <summary>El dosier con el contenido, o null.</summary>
        Task<DosierEvento> Leer(int evento);
        /// <summary>Sustituye el que hubiera. Devuelve la fecha nueva.</summary>
        Task<DateTime> Guardar(int evento, DosierEvento dosier, string usuario);
        /// <summary>False si no tenía.</summary>
        Task<bool> Borrar(int evento);
        /// <summary>De esas inscripciones, las que ya lo tienen.</summary>
        Task<HashSet<int>> Enviados(IReadOnlyCollection<int> inscripciones);
        /// <summary>
        /// Lo aparta para una inscripción (UPDATE … WHERE DosierEnviado IS NULL): true si es para esta pasada. Dos pasadas a la vez
        /// (el job y una inscripción tardía) nunca lo mandan dos veces.
        /// </summary>
        Task<bool> Reservar(int inscripcion);
        /// <summary>El envío ha fallado: se suelta para que lo reintente la siguiente pasada.</summary>
        Task Soltar(int inscripcion);
    }

    public class RepositorioDosierEventos : IRepositorioDosierEventos
    {
        internal const string SQL_EXISTE = "SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.EventosDosier', N'U') IS NULL " +
            "OR COL_LENGTH('dbo.EventosInscripciones', 'DosierEnviado') IS NULL THEN 0 ELSE 1 END AS bit)";

        private static readonly ComprobadorEsquemaEventos esquema = new ComprobadorEsquemaEventos(SQL_EXISTE);
        private readonly NVEntities db;

        public RepositorioDosierEventos(NVEntities db)
        {
            this.db = db;
        }

        public bool Disponible() => esquema.Existe(db);

        public async Task<Dictionary<int, DosierEventoDTO>> LeerInfo(IReadOnlyCollection<int> eventos)
        {
            List<int> ids = (eventos ?? new List<int>()).Distinct().ToList();
            if (!ids.Any() || !Disponible())
            {
                return new Dictionary<int, DosierEventoDTO>();
            }
            var resultado = new Dictionary<int, DosierEventoDTO>();
            foreach (List<int> tanda in RepositorioPersonaAsistenteEventos.Tandas(ids, 500))
            {
                // Los ids son int: se pueden componer en el IN sin riesgo de inyección
                string lista = string.Join(",", tanda);
                foreach (DosierEventoDTO fila in await db.Database.SqlQuery<DosierEventoDTO>(
                    "SELECT d.EventoId, d.Nombre, d.Tamano, d.Fecha, d.Usuario, CAST(NULL AS datetime) AS EnvioPrevisto, " +
                    "(SELECT COUNT(*) FROM dbo.EventosInscripciones i WITH (NOLOCK) WHERE i.EventoId = d.EventoId AND i.DosierEnviado IS NOT NULL) AS Enviados " +
                    "FROM dbo.EventosDosier d WITH (NOLOCK) WHERE d.EventoId IN (" + lista + ")").ToListAsync().ConfigureAwait(false))
                {
                    fila.Nombre = fila.Nombre?.Trim();
                    fila.Usuario = fila.Usuario?.Trim();
                    resultado[fila.EventoId] = fila;
                }
            }
            return resultado;
        }

        public async Task<DosierEvento> Leer(int evento)
        {
            if (!Disponible())
            {
                return null;
            }
            return await db.Database.SqlQuery<DosierEvento>(
                "SELECT EventoId, Nombre, Contenido, Fecha, Usuario FROM dbo.EventosDosier WHERE EventoId = @id",
                new SqlParameter("@id", evento)).FirstOrDefaultAsync().ConfigureAwait(false);
        }

        public async Task<DateTime> Guardar(int evento, DosierEvento dosier, string usuario)
        {
            // UPDLOCK + HOLDLOCK: dos subidas a la vez al mismo evento no chocan con la clave primaria (como las imágenes, #690)
            return await db.Database.SqlQuery<DateTime>(@"
                SET XACT_ABORT ON;
                BEGIN TRAN;
                DECLARE @fecha datetime = GETDATE();
                IF EXISTS (SELECT 1 FROM dbo.EventosDosier WITH (UPDLOCK, HOLDLOCK) WHERE EventoId = @evento)
                    UPDATE dbo.EventosDosier SET Nombre = @nombre, Tamano = @tamano, Contenido = @contenido, Usuario = @usuario, Fecha = @fecha
                    WHERE EventoId = @evento;
                ELSE
                    INSERT INTO dbo.EventosDosier (EventoId, Nombre, Tamano, Contenido, Usuario, Fecha)
                    VALUES (@evento, @nombre, @tamano, @contenido, @usuario, @fecha);
                COMMIT;
                SELECT @fecha;",
                new SqlParameter("@evento", evento),
                new SqlParameter("@nombre", SqlDbType.NVarChar, 200) { Value = dosier.Nombre },
                new SqlParameter("@tamano", dosier.Contenido.Length),
                new SqlParameter("@contenido", SqlDbType.VarBinary, -1) { Value = dosier.Contenido },
                new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = (object)Recortar(usuario, 50) ?? DBNull.Value })
                .SingleAsync().ConfigureAwait(false);
        }

        public async Task<bool> Borrar(int evento) =>
            Disponible() && await db.Database.ExecuteSqlCommandAsync("DELETE FROM dbo.EventosDosier WHERE EventoId = @id",
                new SqlParameter("@id", evento)).ConfigureAwait(false) > 0;

        public async Task<HashSet<int>> Enviados(IReadOnlyCollection<int> inscripciones)
        {
            var resultado = new HashSet<int>();
            List<int> ids = (inscripciones ?? new List<int>()).Distinct().ToList();
            if (!ids.Any() || !Disponible())
            {
                return resultado;
            }
            foreach (List<int> tanda in RepositorioPersonaAsistenteEventos.Tandas(ids, 500))
            {
                foreach (int id in await db.Database.SqlQuery<int>("SELECT Id FROM dbo.EventosInscripciones WITH (NOLOCK) WHERE DosierEnviado IS NOT NULL AND Id IN ("
                    + string.Join(",", tanda) + ")").ToListAsync().ConfigureAwait(false))
                {
                    _ = resultado.Add(id);
                }
            }
            return resultado;
        }

        public async Task<bool> Reservar(int inscripcion) =>
            await db.Database.ExecuteSqlCommandAsync("UPDATE dbo.EventosInscripciones SET DosierEnviado = GETDATE() WHERE Id = @id AND DosierEnviado IS NULL",
                new SqlParameter("@id", inscripcion)).ConfigureAwait(false) == 1;

        public async Task Soltar(int inscripcion) =>
            _ = await db.Database.ExecuteSqlCommandAsync("UPDATE dbo.EventosInscripciones SET DosierEnviado = NULL WHERE Id = @id",
                new SqlParameter("@id", inscripcion)).ConfigureAwait(false);

        private static string Recortar(string texto, int longitud)
        {
            string limpio = texto?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            return limpio.Length <= longitud ? limpio : limpio.Substring(0, longitud);
        }
    }

    /// <summary>Dependencias del envío del dosier (inyectables en los tests).</summary>
    internal class DependenciasDosierEventos
    {
        /// <summary>La hora de Madrid.</summary>
        public Func<DateTime> AhoraMadrid { get; set; }
        public IRepositorioDosierEventos Repositorio { get; set; }
        /// <summary>(día) → los eventos activos de ese día.</summary>
        public Func<DateTime, Task<List<Evento>>> EventosDelDia { get; set; }
        /// <summary>(evento) → el evento, o null.</summary>
        public Func<int, Task<Evento>> Evento { get; set; }
        /// <summary>(evento) → sus inscripciones (todas; la regla decide).</summary>
        public Func<int, Task<List<EventoInscripcion>>> Inscripciones { get; set; }
        /// <summary>(empresa, cliente, contacto) → el correo del cliente (el del recordatorio), o null.</summary>
        public Func<string, string, string, Task<string>> CorreoCliente { get; set; }
        public Func<string, string, string, Task<string>> NombreCliente { get; set; }
        public IServicioCorreoElectronico Correo { get; set; }
    }

    /// <summary>Lo que ha hecho una pasada (tests y diagnóstico).</summary>
    public class ResultadoDosierEventos
    {
        public int Eventos { get; set; }
        public int Enviados { get; set; }
        public int SinCorreo { get; set; }
        public int Fallidos { get; set; }
        /// <summary>Al sustituirlo pidiendo reenviarlo: a cuántas que ya lo tenían se les ha quitado la marca para mandarles el nuevo.</summary>
        public int Reenviados { get; set; }
        public string MotivoNoSeEnvia { get; set; }
    }

    /// <summary>
    /// Sugerencia 1332: el envío del dosier. Se dispara de tres formas, y las tres pasan por el mismo núcleo
    /// (<see cref="EnviarDelEvento"/>):
    /// <list type="bullet">
    /// <item>Job recurrente «eventos-dosier» cada 15 minutos de 7:00 a 21:45 (hora de Madrid): mira los eventos de HOY y, a los que
    /// <see cref="ReglasDosierEvento.TocaEnviar"/>, les manda el dosier a quien no lo tenga. Se mira el estado en cada pasada (no se
    /// programa nada al guardar): si cambia la hora del evento, vale la nueva; si la API se reinicia o una pasada falla, la siguiente
    /// lo hace; nada se manda dos veces (marca por inscripción, apartada con un UPDATE condicional antes de mandarlo).</item>
    /// <item>Al subir (o sustituir) el dosier: si ya es la hora, una pasada de ese evento al momento. Sin reenviar, solo a quien no
    /// lo tenga; con reenviar (Nesto lo pregunta al sustituirlo), también a las que ya tenían el anterior, con el texto de versión
    /// actualizada (<see cref="ReglasDosierEvento.AReenviar"/>).</item>
    /// <item>Al inscribirse después (o quedar pagada), por <see cref="AvisosInscripcionEventosJobs"/>.</item>
    /// </list>
    /// </summary>
    public static class DosierEventosJobs
    {
        /// <summary>Cada 15 minutos de 7:00 a 21:45, hora de Madrid. Un evento a las 23:00 lo recibe desde las 21:00.</summary>
        public const string CRON = "*/15 7-21 * * *";
        internal const string MOTIVO_SIN_SCRIPT = "Falta el script Scripts/EventosDosier.sql.";

        /// <summary>Encola una pasada de un evento (al subir el dosier; con <paramref name="reenviar"/>, también a quien ya lo tenía). Nunca lanza.</summary>
        public static void Encolar(int eventoId, bool reenviar)
        {
            try
            {
                _ = BackgroundJob.Enqueue(() => Procesar(eventoId, reenviar));
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Dosier de eventos] No se ha podido encolar el envío del dosier del evento {eventoId}: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// La pasada: con <paramref name="eventoId"/>, solo ese evento (con <paramref name="reenviar"/>, también a las que ya tenían
        /// el dosier anterior); sin él (el job recurrente), los de hoy.
        /// </summary>
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static async Task Procesar(int? eventoId, bool reenviar)
        {
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    DependenciasDosierEventos deps = Dependencias(db);
                    _ = eventoId.HasValue
                        ? await ProcesarEvento(deps, eventoId.Value, reenviar).ConfigureAwait(false)
                        : await ProcesarHoy(deps).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Dosier de eventos] Error en el envío del dosier: {ex.Message}", ex));
                throw;
            }
        }

        internal static DependenciasDosierEventos Dependencias(NVEntities db) => new DependenciasDosierEventos
        {
            AhoraMadrid = () => ReglasDosierEvento.AhoraMadrid(DateTime.UtcNow),
            Repositorio = new RepositorioDosierEventos(db),
            EventosDelDia = async dia =>
            {
                DateTime desde = dia.Date;
                DateTime hasta = desde.AddDays(1);
                return await db.Eventos.Where(e => e.Activo && e.Fecha >= desde && e.Fecha < hasta).ToListAsync().ConfigureAwait(false);
            },
            Evento = id => db.Eventos.SingleOrDefaultAsync(e => e.Id == id),
            Inscripciones = id => db.EventosInscripciones.Where(i => i.EventoId == id).ToListAsync(),
            CorreoCliente = (empresa, cliente, contacto) => RecordatorioEventosJobsService.CorreoDelCliente(db, empresa, cliente, contacto),
            NombreCliente = (empresa, cliente, contacto) => RecordatorioEventosJobsService.NombreDelCliente(db, empresa, cliente, contacto),
            Correo = new ServicioCorreoElectronico()
        };

        /// <summary>El job recurrente: los eventos de hoy a los que ya les toca.</summary>
        internal static async Task<ResultadoDosierEventos> ProcesarHoy(DependenciasDosierEventos deps)
        {
            var resultado = new ResultadoDosierEventos();
            if (!deps.Repositorio.Disponible())
            {
                resultado.MotivoNoSeEnvia = MOTIVO_SIN_SCRIPT;
                return resultado;
            }
            DateTime ahora = deps.AhoraMadrid();
            foreach (Evento evento in (await deps.EventosDelDia(ahora.Date).ConfigureAwait(false) ?? new List<Evento>())
                .Where(e => ReglasDosierEvento.TocaEnviar(e, ahora)))
            {
                await EnviarDelEvento(deps, evento, null, ahora, resultado).ConfigureAwait(false);
            }
            return resultado;
        }

        /// <summary>
        /// Una pasada de un evento (al subir el dosier). Con <paramref name="reenviar"/>, antes se les quita la marca a las que ya lo
        /// tenían (<see cref="ReglasDosierEvento.AReenviar"/>), y a esas les llega con el texto de versión actualizada.
        /// </summary>
        internal static async Task<ResultadoDosierEventos> ProcesarEvento(DependenciasDosierEventos deps, int eventoId, bool reenviar = false)
        {
            var resultado = new ResultadoDosierEventos();
            if (!deps.Repositorio.Disponible())
            {
                resultado.MotivoNoSeEnvia = MOTIVO_SIN_SCRIPT;
                return resultado;
            }
            DateTime ahora = deps.AhoraMadrid();
            Evento evento = await deps.Evento(eventoId).ConfigureAwait(false);
            if (!ReglasDosierEvento.TocaEnviar(evento, ahora))
            {
                return resultado;
            }
            var actualizadas = new HashSet<int>();
            if (reenviar)
            {
                List<EventoInscripcion> todas = await deps.Inscripciones(evento.Id).ConfigureAwait(false) ?? new List<EventoInscripcion>();
                HashSet<int> yaLoTienen = await deps.Repositorio.Enviados(todas.Select(i => i.Id).ToList()).ConfigureAwait(false) ?? new HashSet<int>();
                foreach (int id in ReglasDosierEvento.AReenviar(evento, todas, yaLoTienen, ahora))
                {
                    await deps.Repositorio.Soltar(id).ConfigureAwait(false);
                    _ = actualizadas.Add(id);
                }
                resultado.Reenviados = actualizadas.Count;
            }
            await EnviarDelEvento(deps, evento, null, ahora, resultado, actualizadas).ConfigureAwait(false);
            return resultado;
        }

        /// <summary>Al inscribirse (o quedar pagada) cuando ya es la hora del dosier: solo a esa inscripción.</summary>
        internal static async Task<ResultadoDosierEventos> AlInscribirse(DependenciasDosierEventos deps, Evento evento, EventoInscripcion inscripcion)
        {
            var resultado = new ResultadoDosierEventos();
            if (!deps.Repositorio.Disponible())
            {
                resultado.MotivoNoSeEnvia = MOTIVO_SIN_SCRIPT;
                return resultado;
            }
            DateTime ahora = deps.AhoraMadrid();
            if (ReglasDosierEvento.TocaEnviar(evento, ahora))
            {
                await EnviarDelEvento(deps, evento, new List<EventoInscripcion> { inscripcion }, ahora, resultado).ConfigureAwait(false);
            }
            return resultado;
        }

        /// <summary>
        /// EL núcleo: con el dosier del evento (si no hay, nada), a cada inscripción que <see cref="ReglasDosierEvento.LoRecibe"/> y
        /// tiene correo, se le aparta la marca y se le manda; si el correo falla, ELMAH y se suelta la marca (la siguiente pasada lo
        /// reintenta sin repetírselo a las demás).
        /// </summary>
        internal static async Task EnviarDelEvento(DependenciasDosierEventos deps, Evento evento, List<EventoInscripcion> soloEstas,
            DateTime ahora, ResultadoDosierEventos resultado, ISet<int> actualizadas = null)
        {
            DosierEvento dosier = await deps.Repositorio.Leer(evento.Id).ConfigureAwait(false);
            if (dosier?.Contenido == null || dosier.Contenido.Length == 0)
            {
                return;
            }
            resultado.Eventos++;
            List<EventoInscripcion> inscripciones = soloEstas ?? await deps.Inscripciones(evento.Id).ConfigureAwait(false) ?? new List<EventoInscripcion>();
            HashSet<int> enviados = await deps.Repositorio.Enviados(inscripciones.Select(i => i.Id).ToList()).ConfigureAwait(false) ?? new HashSet<int>();
            foreach (EventoInscripcion inscripcion in inscripciones.Where(i => ReglasDosierEvento.LoRecibe(i, enviados.Contains(i.Id))))
            {
                string empresa = inscripcion.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
                string cliente = inscripcion.Cliente?.Trim();
                string contacto = inscripcion.Contacto?.Trim();
                try
                {
                    string destino = await deps.CorreoCliente(empresa, cliente, contacto).ConfigureAwait(false);
                    if (destino == null)
                    {
                        resultado.SinCorreo++;
                        continue;
                    }
                    if (!await deps.Repositorio.Reservar(inscripcion.Id).ConfigureAwait(false))
                    {
                        continue; // Otra pasada se la ha llevado
                    }
                    bool enviado = false;
                    try
                    {
                        string nombre = await deps.NombreCliente(empresa, cliente, contacto).ConfigureAwait(false);
                        using (MailMessage mail = ReglasDosierEvento.Correo(evento, dosier, nombre, destino, ahora,
                            actualizadas?.Contains(inscripcion.Id) == true))
                        {
                            enviado = deps.Correo.EnviarCorreoSMTP(mail);
                        }
                    }
                    finally
                    {
                        if (!enviado)
                        {
                            await deps.Repositorio.Soltar(inscripcion.Id).ConfigureAwait(false);
                        }
                    }
                    if (enviado)
                    {
                        resultado.Enviados++;
                    }
                    else
                    {
                        resultado.Fallidos++;
                        ElmahHelper.Log(new Exception($"[Dosier de eventos] No se ha podido mandar el dosier de «{evento.Titulo?.Trim()}» a {destino} (inscripción {inscripcion.Id}); se reintenta en la siguiente pasada."));
                    }
                }
                catch (Exception ex)
                {
                    resultado.Fallidos++;
                    ElmahHelper.Log(new Exception($"[Dosier de eventos] Inscripción {inscripcion.Id}: {ex.Message}; se reintenta en la siguiente pasada.", ex));
                }
            }
        }
    }
}
