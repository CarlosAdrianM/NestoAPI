using Hangfire;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>NestoAPI#692: cuerpo de POST api/Eventos/{id}/Anunciar.</summary>
    public class PeticionAnuncioEventoDTO
    {
        /// <summary>
        /// Opcional la primera vez; obligatorio para volver a anunciar (otraVez=true). Hasta 60 caracteres. Va de título de la
        /// push («¡Últimas plazas disponibles!»). Se elige de GET api/Eventos/{id}/Reclamos o se escribe a mano.
        /// </summary>
        public string Reclamo { get; set; }
    }

    /// <summary>NestoAPI#692: lo que devuelve POST api/Eventos/{id}/Anunciar.</summary>
    public class ResultadoAnuncioEventoDTO
    {
        public int EventoId { get; set; }
        public DateTime AnunciadoFecha { get; set; }
        public string AnunciadoUsuario { get; set; }
        public string Reclamo { get; set; }
        /// <summary>Fuera de 9:00-21:00: no se ha mandado aún, sale a las 9:00 (<see cref="ProgramadoPara"/>).</summary>
        public bool Encolado { get; set; }
        public DateTime? ProgramadoPara { get; set; }
        public int DispositivosNestoApp { get; set; }
        public int DispositivosTiendas { get; set; }
        /// <summary>Clientes de TNV a los que se ha mandado (con la zona y su interruptor ya aplicados).</summary>
        public int ClientesTiendas { get; set; }
        public int Dispositivos => DispositivosNestoApp + DispositivosTiendas;
        /// <summary>Texto de usted para enseñar tal cual en Nesto.</summary>
        public string Mensaje { get; set; }
    }

    /// <summary>NestoAPI#692: el último anuncio de un evento (Eventos.AnunciadoFecha / AnunciadoUsuario).</summary>
    public class AnuncioEventoLeido
    {
        public int EventoId { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
    }

    /// <summary>NestoAPI#692: una fila de dbo.EventosAnuncios (cada vez que se anuncia, también los encolados).</summary>
    public class AnuncioEventoRegistrado
    {
        public int Id { get; set; }
        public int EventoId { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
        public string Reclamo { get; set; }
        public DateTime? ProgramadoPara { get; set; }
        public DateTime? EnviadoFecha { get; set; }
    }

    /// <summary>NestoAPI#692: un cliente con la app de la tienda (NestoTiendas) y los códigos postales de sus fichas.</summary>
    public class ClienteAppTiendas
    {
        public string Cliente { get; set; }
        /// <summary>El de la ficha del contacto de sus dispositivos; si no casa con ninguna, los de todas sus fichas.</summary>
        public List<string> CodigosPostales { get; set; } = new List<string>();
    }

    /// <summary>
    /// NestoAPI#692: las reglas del anuncio: cuándo está completo un evento, qué reclamos se proponen, la franja horaria, a qué
    /// clientes de TNV va y los textos de la push (de usted).
    /// </summary>
    public static class ReglasAnuncioEvento
    {
        public const string TIPO_NOTIFICACION = "AnuncioEvento";
        public const int LONGITUD_RECLAMO = 60;
        public const string ERROR_INCOMPLETO = "EVENTO_INCOMPLETO";
        public const string ERROR_YA_ANUNCIADO = "EVENTO_YA_ANUNCIADO";
        internal static readonly TimeSpan HORA_DESDE = new TimeSpan(9, 0, 0);
        internal static readonly TimeSpan HORA_HASTA = new TimeSpan(21, 0, 0);

        public const string RECLAMO_ULTIMAS_PLAZAS = "¡Últimas plazas disponibles!";
        public const string RECLAMO_A_TIEMPO = "¡Aún está a tiempo!";

        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");
        private static readonly string[] numeros = { "cero", "un", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve", "diez" };

        /// <summary>
        /// Lo que le falta al evento para poder anunciarlo (frases en minúscula para «falta: …»); vacía si está completo. Decisión
        /// de Carlos (22/10/26): título, fecha (que no haya pasado), horas, tienda, formato, ponente, descripción, plazas, producto
        /// del curso (los gratuitos no lo necesitan: no se facturan), imagen y, en los online, el enlace en «Lugar». Los importes
        /// siempre valen: con los dos a 0 el evento es gratuito. Tampoco se anuncia uno desactivado.
        /// </summary>
        public static List<string> QueFalta(EventoDTO evento, DateTime hoy)
        {
            var falta = new List<string>();
            if (evento == null)
            {
                falta.Add("los datos del evento");
                return falta;
            }
            if (!evento.Activo)
            {
                falta.Add("que el evento esté activo");
            }
            if (string.IsNullOrWhiteSpace(evento.Titulo))
            {
                falta.Add("el título");
            }
            if (evento.Fecha.Year < 2000)
            {
                falta.Add("la fecha");
            }
            else if (evento.Fecha.Date < hoy.Date)
            {
                falta.Add("una fecha que no haya pasado");
            }
            if (string.IsNullOrWhiteSpace(evento.HoraInicio))
            {
                falta.Add("la hora de inicio");
            }
            if (string.IsNullOrWhiteSpace(evento.HoraFin))
            {
                falta.Add("la hora de fin");
            }
            if (!PermisosEventos.EsAlmacenDeEventos(evento.Almacen))
            {
                falta.Add("la tienda");
            }
            if (string.IsNullOrWhiteSpace(evento.Formato))
            {
                falta.Add("el formato");
            }
            if (string.IsNullOrWhiteSpace(evento.Ponente))
            {
                falta.Add("el ponente");
            }
            if (string.IsNullOrWhiteSpace(evento.DescripcionLarga))
            {
                falta.Add("la descripción");
            }
            if (!evento.Plazas.HasValue)
            {
                falta.Add("las plazas");
            }
            if (!evento.Gratuito && string.IsNullOrWhiteSpace(evento.ProductoCurso))
            {
                falta.Add("el producto del curso");
            }
            if (string.IsNullOrWhiteSpace(evento.ImagenUrl))
            {
                falta.Add("la imagen");
            }
            if (EsOnline(evento) && FormatosEvento.EnlaceOnline(AEntidad(evento)) == null)
            {
                falta.Add("el enlace de conexión en «Lugar»");
            }
            return falta;
        }

        /// <summary>Pone Completo y QueFalta en el DTO (después de ImagenUrl y Gratuito).</summary>
        public static EventoDTO Completar(EventoDTO evento, DateTime hoy)
        {
            if (evento != null)
            {
                evento.QueFalta = QueFalta(evento, hoy);
                evento.Completo = evento.QueFalta.Count == 0;
            }
            return evento;
        }

        /// <summary>
        /// Los reclamos que se proponen para volver a anunciar, el más fuerte antes (decisión de Carlos, 22/10/26): «Últimas
        /// plazas» si quedan pocas (como mucho 3, o el 20 % de las plazas), «Solo faltan N días» si falta poco, y otros que
        /// siempre valen. Todos de usted y de como mucho <see cref="LONGITUD_RECLAMO"/> caracteres.
        /// </summary>
        public static List<string> Reclamos(EventoDTO evento, DateTime hoy)
        {
            var reclamos = new List<string>();
            if (evento == null)
            {
                return reclamos;
            }
            if (evento.Plazas.HasValue)
            {
                int libres = evento.Plazas.Value - evento.PlazasOcupadas;
                int pocas = Math.Max(3, (int)Math.Ceiling(evento.Plazas.Value * 0.2m));
                if (libres > 0 && libres <= pocas)
                {
                    reclamos.Add(RECLAMO_ULTIMAS_PLAZAS);
                    reclamos.Add(libres == 1 ? "¡Solo queda una plaza!" : $"¡Solo quedan {Numero(libres)} plazas!");
                }
            }
            int dias = (evento.Fecha.Date - hoy.Date).Days;
            if (dias == 0)
            {
                reclamos.Add("¡Es hoy! Aún está a tiempo");
            }
            else if (dias == 1)
            {
                reclamos.Add("¡Es mañana! Aún está a tiempo");
            }
            else if (dias > 1 && dias <= 30)
            {
                reclamos.Add($"¡Solo faltan {Numero(dias)} días!");
            }
            if (dias >= 0)
            {
                reclamos.Add(RECLAMO_A_TIEMPO);
            }
            if (evento.Gratuito)
            {
                reclamos.Add("¡Es gratuito! Reserve su plaza");
            }
            if (EsOnline(evento))
            {
                reclamos.Add("¡Online: desde donde usted quiera!");
            }
            reclamos.Add("¡No se lo pierda!");
            return reclamos.Where(r => r.Length <= LONGITUD_RECLAMO).Distinct().ToList();
        }

        /// <summary>«dos», «diez», 15.</summary>
        internal static string Numero(int n) => n >= 2 && n < numeros.Length ? numeros[n] : n.ToString(CultureInfo.InvariantCulture);

        /// <summary>Las push solo salen entre las 9:00 y las 21:00.</summary>
        public static bool DentroDeHorario(DateTime ahora) => ahora.TimeOfDay >= HORA_DESDE && ahora.TimeOfDay < HORA_HASTA;

        /// <summary>Cuándo puede salir: ya si está en la franja; antes de las 9:00, hoy a las 9:00; desde las 21:00, mañana a las 9:00.</summary>
        public static DateTime ProximaFranja(DateTime ahora)
        {
            if (DentroDeHorario(ahora))
            {
                return ahora;
            }
            return ahora.TimeOfDay < HORA_DESDE ? ahora.Date.Add(HORA_DESDE) : ahora.Date.AddDays(1).Add(HORA_DESDE);
        }

        public static bool EsOnline(EventoDTO evento) =>
            string.Equals(evento?.Formato?.Trim(), FormatosEvento.ONLINE, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// La zona de los presenciales (decisión de Carlos, 22/10/26: «Madrid y alrededores»). Es la misma que ya usan los portes
        /// provinciales (NestoAPI#488, <see cref="GestorPortes.EsProvincial"/>): Madrid (28), Guadalajara (19) y Toledo (45).
        /// </summary>
        public static bool EnZonaMadrid(string codigoPostal)
        {
            string cp = codigoPostal?.Trim();
            return cp != null && cp.Length == 5 && GestorPortes.EsProvincial(cp);
        }

        /// <summary>
        /// A qué clientes de TNV va: sin los que han apagado «Eventos y cursos» y, si el evento es presencial, solo los de la zona
        /// de Madrid (alguna de sus fichas). Los online, a todos.
        /// </summary>
        public static List<string> Destinatarios(IEnumerable<ClienteAppTiendas> clientes, ISet<string> deBaja, bool online)
        {
            return (clientes ?? Enumerable.Empty<ClienteAppTiendas>())
                .Where(c => !string.IsNullOrWhiteSpace(c?.Cliente))
                .Where(c => deBaja == null || !deBaja.Contains(c.Cliente.Trim()))
                .Where(c => online || (c.CodigosPostales ?? new List<string>()).Any(EnZonaMadrid))
                .Select(c => c.Cliente.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>«Nuevo evento: «Título»», o el reclamo si se vuelve a anunciar.</summary>
        public static string Titulo(EventoDTO evento, string reclamo) =>
            string.IsNullOrWhiteSpace(reclamo) ? $"Nuevo evento: «{evento.Titulo?.Trim()}»" : reclamo.Trim();

        /// <summary>
        /// «El jueves 30 de octubre a las 10:00 en Reina.» (con reclamo: «Masterclass X», el jueves…). Para TNV, además, «Puede
        /// reservar su plaza en la app.»
        /// </summary>
        public static string Cuerpo(EventoDTO evento, string reclamo, bool paraClientes)
        {
            string cuando = RecordatorioEventosJobsService.CuandoYDonde(AEntidad(evento));
            string texto = string.IsNullOrWhiteSpace(reclamo)
                ? char.ToUpper(cuando[0], es) + cuando.Substring(1) + "."
                : $"«{evento.Titulo?.Trim()}», {cuando}.";
            return paraClientes ? texto + " Puede reservar su plaza en la app." : texto;
        }

        /// <summary>La push de TNV: abre //Eventos (TNV, como la de RecordatorioEvento) con la imagen del evento.</summary>
        public static NotificacionPushDTO NotificacionClientes(EventoDTO evento, string reclamo) =>
            Notificacion(evento, reclamo, paraClientes: true);

        /// <summary>La push de NestoApp: tipo AnuncioEvento con el Id del evento (NestoApp#247).</summary>
        public static NotificacionPushDTO NotificacionVendedores(EventoDTO evento, string reclamo) =>
            Notificacion(evento, reclamo, paraClientes: false);

        private static NotificacionPushDTO Notificacion(EventoDTO evento, string reclamo, bool paraClientes)
        {
            var datos = new Dictionary<string, string>
            {
                ["tipo"] = TIPO_NOTIFICACION,
                ["eventoId"] = evento.Id.ToString(CultureInfo.InvariantCulture),
                ["fecha"] = evento.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["formato"] = evento.Formato ?? FormatosEvento.PRESENCIAL
            };
            if (!string.IsNullOrWhiteSpace(evento.ImagenUrl))
            {
                datos["imagenUrl"] = evento.ImagenUrl;
            }
            return new NotificacionPushDTO
            {
                Titulo = Titulo(evento, reclamo),
                Cuerpo = Cuerpo(evento, reclamo, paraClientes),
                Tipo = TIPO_NOTIFICACION,
                Datos = datos
            };
        }

        /// <summary>«Anunciado en 37 dispositivos (12 de NestoApp y 25 de la tienda online).»</summary>
        public static string Mensaje(ResultadoAnuncioEventoDTO resultado, DateTime ahora)
        {
            if (resultado.Encolado && resultado.ProgramadoPara.HasValue)
            {
                string dia = resultado.ProgramadoPara.Value.Date == ahora.Date ? "hoy" : "mañana";
                return $"Las notificaciones solo salen de 9:00 a 21:00: el anuncio saldrá {dia} a las 9:00.";
            }
            if (resultado.Dispositivos == 0)
            {
                return "Anunciado, pero no ha llegado a ningún dispositivo.";
            }
            return $"Anunciado en {Dispositivos(resultado.Dispositivos)} ({resultado.DispositivosNestoApp} de NestoApp y " +
                $"{resultado.DispositivosTiendas} de la tienda online).";
        }

        private static string Dispositivos(int n) => n == 1 ? "1 dispositivo" : $"{n} dispositivos";

        /// <summary>El evento del DTO para reutilizar CuandoYDonde y EnlaceOnline (que trabajan con la entidad).</summary>
        internal static Evento AEntidad(EventoDTO e) => new Evento
        {
            Id = e.Id,
            Titulo = e.Titulo,
            Fecha = e.Fecha,
            HoraInicio = string.IsNullOrWhiteSpace(e.HoraInicio) ? (TimeSpan?)null : ReglasModalidadEvento.LeerHora(e.HoraInicio, "inicio"),
            Formato = e.Formato,
            Lugar = e.Lugar,
            Almacen = e.Almacen
        };
    }

    /// <summary>NestoAPI#692: Eventos.AnunciadoFecha/AnunciadoUsuario y dbo.EventosAnuncios (SQL a mano, fuera del EDMX).</summary>
    public interface IRepositorioAnunciosEventos
    {
        /// <summary>El último anuncio de esos eventos (solo los anunciados).</summary>
        Task<Dictionary<int, AnuncioEventoLeido>> LeerAnunciados(IEnumerable<int> eventos);
        /// <summary>
        /// Apunta el anuncio (en el evento y en el histórico). Si no es <paramref name="otraVez"/>, solo si el evento no estaba
        /// anunciado: null si otra persona lo acaba de anunciar. Devuelve el Id del anuncio.
        /// </summary>
        Task<int?> Registrar(int eventoId, DateTime fecha, string usuario, string reclamo, DateTime? programadoPara, bool otraVez);
        Task<AnuncioEventoRegistrado> Leer(int anuncioId);
        /// <summary>Registra a cuántos dispositivos (y clientes de TNV) se mandó.</summary>
        Task MarcarEnviado(int anuncioId, DateTime fecha, int dispositivosNestoApp, int dispositivosTiendas, int clientesTiendas);
        /// <summary>Los clientes con algún dispositivo activo de NestoTiendas, con los códigos postales de sus fichas.</summary>
        Task<List<ClienteAppTiendas>> ClientesConAppTiendas(string empresa);
    }

    public class RepositorioAnunciosEventosSql : IRepositorioAnunciosEventos
    {
        internal const string SQL_REGISTRAR = @"
SET NOCOUNT ON;
UPDATE dbo.Eventos SET AnunciadoFecha = @fecha, AnunciadoUsuario = @usuario
WHERE Id = @eventoId AND (@otraVez = 1 OR AnunciadoFecha IS NULL);
IF @@ROWCOUNT = 0
    SELECT CAST(NULL AS int);
ELSE
BEGIN
    INSERT INTO dbo.EventosAnuncios (EventoId, Fecha, Usuario, Reclamo, ProgramadoPara)
    VALUES (@eventoId, @fecha, @usuario, @reclamo, @programadoPara);
    SELECT CAST(SCOPE_IDENTITY() AS int);
END";

        internal const string SQL_LEER = @"
SELECT Id, EventoId, Fecha, RTRIM(Usuario) AS Usuario, Reclamo, ProgramadoPara, EnviadoFecha
FROM dbo.EventosAnuncios WHERE Id = @id";

        internal const string SQL_MARCAR_ENVIADO = @"
UPDATE dbo.EventosAnuncios
SET EnviadoFecha = @fecha, DispositivosNestoApp = @nestoApp, DispositivosTiendas = @tiendas, ClientesTiendas = @clientes
WHERE Id = @id";

        private readonly NVEntities db;

        public RepositorioAnunciosEventosSql(NVEntities db)
        {
            this.db = db;
        }

        private class FilaAnunciado
        {
            public int Id { get; set; }
            public DateTime AnunciadoFecha { get; set; }
            public string AnunciadoUsuario { get; set; }
        }

        public async Task<Dictionary<int, AnuncioEventoLeido>> LeerAnunciados(IEnumerable<int> eventos)
        {
            List<int> ids = (eventos ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (!ids.Any())
            {
                return new Dictionary<int, AnuncioEventoLeido>();
            }
            // Son enteros: van en la consulta sin riesgo de inyección (un IN con parámetros sería uno por evento)
            string sql = "SELECT Id, AnunciadoFecha, RTRIM(AnunciadoUsuario) AS AnunciadoUsuario FROM dbo.Eventos " +
                "WHERE AnunciadoFecha IS NOT NULL AND Id IN (" + string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ")";
            List<FilaAnunciado> filas = await db.Database.SqlQuery<FilaAnunciado>(sql).ToListAsync().ConfigureAwait(false);
            return filas.ToDictionary(f => f.Id, f => new AnuncioEventoLeido { EventoId = f.Id, Fecha = f.AnunciadoFecha, Usuario = f.AnunciadoUsuario });
        }

        public async Task<int?> Registrar(int eventoId, DateTime fecha, string usuario, string reclamo, DateTime? programadoPara, bool otraVez)
        {
            List<int?> id = await db.Database.SqlQuery<int?>(SQL_REGISTRAR,
                new SqlParameter("@eventoId", eventoId),
                new SqlParameter("@fecha", fecha),
                new SqlParameter("@usuario", Texto(usuario, 50)),
                new SqlParameter("@reclamo", Texto(reclamo, ReglasAnuncioEvento.LONGITUD_RECLAMO)),
                new SqlParameter("@programadoPara", (object)programadoPara ?? DBNull.Value),
                new SqlParameter("@otraVez", otraVez)).ToListAsync().ConfigureAwait(false);
            return id.FirstOrDefault();
        }

        public async Task<AnuncioEventoRegistrado> Leer(int anuncioId) =>
            (await db.Database.SqlQuery<AnuncioEventoRegistrado>(SQL_LEER, new SqlParameter("@id", anuncioId))
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task MarcarEnviado(int anuncioId, DateTime fecha, int dispositivosNestoApp, int dispositivosTiendas, int clientesTiendas)
            => _ = await db.Database.ExecuteSqlCommandAsync(SQL_MARCAR_ENVIADO,
                new SqlParameter("@id", anuncioId),
                new SqlParameter("@fecha", fecha),
                new SqlParameter("@nestoApp", dispositivosNestoApp),
                new SqlParameter("@tiendas", dispositivosTiendas),
                new SqlParameter("@clientes", clientesTiendas)).ConfigureAwait(false);

        public async Task<List<ClienteAppTiendas>> ClientesConAppTiendas(string empresa)
        {
            string aplicacion = Constantes.Aplicaciones.NESTO_TIENDAS;
            var dispositivos = await db.DispositivosNotificaciones
                .Where(d => d.Aplicacion == aplicacion && d.Activo && d.Empresa == empresa && d.Cliente != null)
                .Select(d => new { d.Cliente, d.Contacto })
                .Distinct()
                .ToListAsync().ConfigureAwait(false);
            List<string> clientes = dispositivos.Select(d => d.Cliente.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!clientes.Any())
            {
                return new List<ClienteAppTiendas>();
            }
            var fichas = await db.Clientes
                .Where(c => c.Empresa == empresa && clientes.Contains(c.Nº_Cliente))
                .Select(c => new { c.Nº_Cliente, c.Contacto, c.CodPostal })
                .ToListAsync().ConfigureAwait(false);
            return clientes.Select(cliente =>
            {
                var suyas = fichas.Where(f => string.Equals(f.Nº_Cliente?.Trim(), cliente, StringComparison.OrdinalIgnoreCase)).ToList();
                var contactos = new HashSet<string>(dispositivos
                    .Where(d => string.Equals(d.Cliente.Trim(), cliente, StringComparison.OrdinalIgnoreCase) && d.Contacto != null)
                    .Select(d => d.Contacto.Trim()));
                var deSusDispositivos = suyas.Where(f => contactos.Contains(f.Contacto?.Trim() ?? "")).ToList();
                return new ClienteAppTiendas
                {
                    Cliente = cliente,
                    CodigosPostales = (deSusDispositivos.Any() ? deSusDispositivos : suyas)
                        .Select(f => f.CodPostal?.Trim()).Where(cp => !string.IsNullOrEmpty(cp)).Distinct().ToList()
                };
            }).ToList();
        }

        private static object Texto(string valor, int maximo)
        {
            string limpio = valor?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return DBNull.Value;
            }
            return limpio.Length > maximo ? limpio.Substring(0, maximo) : limpio;
        }
    }

    public interface IServicioAnunciosEventos
    {
        /// <summary>
        /// Anuncia el evento en NestoApp y TNV (o lo deja programado para las 9:00 si es fuera de 9:00-21:00). 404 si no existe;
        /// 400 <see cref="ReglasAnuncioEvento.ERROR_INCOMPLETO"/> con lo que falta; 409
        /// <see cref="ReglasAnuncioEvento.ERROR_YA_ANUNCIADO"/> si ya se anunció y no es <paramref name="otraVez"/>.
        /// </summary>
        Task<ResultadoAnuncioEventoDTO> Anunciar(int eventoId, bool otraVez, string reclamo, string usuario);
        Task<List<string>> Reclamos(int eventoId);
        /// <summary>El job de las 9:00: manda un anuncio que se pidió fuera de horario. Null si ya no hay que mandarlo.</summary>
        Task<ResultadoAnuncioEventoDTO> EnviarProgramado(int anuncioId);
    }

    /// <summary>
    /// NestoAPI#692: «El evento está completo. ¿Quiere anunciarlo en las apps?». Una push a todos los vendedores de NestoApp y a
    /// los clientes de TNV (los presenciales, solo a los de la zona de Madrid; cada cliente puede apagarlo en «Notificaciones
    /// que recibe»). Nunca fuera de 9:00-21:00: si se pide fuera, sale a las 9:00 por Hangfire.
    /// </summary>
    public class ServicioAnunciosEventos : IServicioAnunciosEventos
    {
        private readonly IServicioEventos eventos;
        private readonly IRepositorioAnunciosEventos anuncios;
        private readonly IServicioNotificacionesPush push;
        private readonly IRepositorioPreferenciasNotificaciones preferencias;
        private readonly Func<DateTime> reloj;
        private readonly Action<int, DateTime> programar;

        /// <param name="programar">(anuncio, cuándo) → lo deja para esa hora. Por defecto, BackgroundJob.Schedule del job.</param>
        public ServicioAnunciosEventos(IServicioEventos eventos, IRepositorioAnunciosEventos anuncios, IServicioNotificacionesPush push,
            IRepositorioPreferenciasNotificaciones preferencias, Func<DateTime> reloj = null, Action<int, DateTime> programar = null)
        {
            this.eventos = eventos;
            this.anuncios = anuncios;
            this.push = push;
            this.preferencias = preferencias;
            this.reloj = reloj ?? (() => DateTime.Now);
            this.programar = programar ?? ((id, cuando) =>
                BackgroundJob.Schedule(() => AnunciosEventosJobsService.EnviarProgramado(id), new DateTimeOffset(cuando)));
        }

        public async Task<List<string>> Reclamos(int eventoId)
        {
            EventoDTO evento = await eventos.LeerEvento(eventoId).ConfigureAwait(false)
                ?? throw new NestoBusinessException($"No existe el evento {eventoId}.") { StatusCode = HttpStatusCode.NotFound };
            return ReglasAnuncioEvento.Reclamos(evento, reloj().Date);
        }

        public async Task<ResultadoAnuncioEventoDTO> Anunciar(int eventoId, bool otraVez, string reclamo, string usuario)
        {
            EventoDTO evento = await eventos.LeerEvento(eventoId).ConfigureAwait(false)
                ?? throw new NestoBusinessException($"No existe el evento {eventoId}.") { StatusCode = HttpStatusCode.NotFound };
            string reclamoLimpio = string.IsNullOrWhiteSpace(reclamo) ? null : reclamo.Trim();
            if (reclamoLimpio != null && reclamoLimpio.Length > ReglasAnuncioEvento.LONGITUD_RECLAMO)
            {
                throw new NestoBusinessException($"El reclamo no puede tener más de {ReglasAnuncioEvento.LONGITUD_RECLAMO} caracteres.");
            }
            if (!evento.Completo)
            {
                throw new NestoBusinessException(
                    $"El evento todavía no se puede anunciar: falta {string.Join(", ", evento.QueFalta)}.",
                    new ErrorContext { ErrorCode = ReglasAnuncioEvento.ERROR_INCOMPLETO }.WithData("queFalta", evento.QueFalta))
                { IsWarning = true };
            }
            if (evento.AnunciadoFecha.HasValue)
            {
                if (!otraVez)
                {
                    throw YaAnunciado(evento.AnunciadoFecha.Value, evento.AnunciadoUsuario);
                }
                if (reclamoLimpio == null)
                {
                    throw new NestoBusinessException(
                        "Para volver a anunciar el evento, elija o escriba un reclamo (por ejemplo, «¡Últimas plazas disponibles!»).");
                }
            }

            DateTime ahora = reloj();
            bool enHorario = ReglasAnuncioEvento.DentroDeHorario(ahora);
            DateTime? programadoPara = enHorario ? (DateTime?)null : ReglasAnuncioEvento.ProximaFranja(ahora);
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            int? anuncioId = await anuncios.Registrar(eventoId, ahora, usuarioAuditoria, reclamoLimpio, programadoPara, otraVez).ConfigureAwait(false);
            if (!anuncioId.HasValue)
            {
                // Otra persona lo ha anunciado entre que se leyó y ahora
                throw YaAnunciado(ahora, null);
            }

            var resultado = new ResultadoAnuncioEventoDTO
            {
                EventoId = eventoId,
                AnunciadoFecha = ahora,
                AnunciadoUsuario = usuarioAuditoria,
                Reclamo = reclamoLimpio,
                Encolado = !enHorario,
                ProgramadoPara = programadoPara
            };
            if (enHorario)
            {
                await Enviar(evento, reclamoLimpio, resultado).ConfigureAwait(false);
                await anuncios.MarcarEnviado(anuncioId.Value, reloj(), resultado.DispositivosNestoApp, resultado.DispositivosTiendas,
                    resultado.ClientesTiendas).ConfigureAwait(false);
            }
            else
            {
                programar(anuncioId.Value, programadoPara.Value);
            }
            resultado.Mensaje = ReglasAnuncioEvento.Mensaje(resultado, ahora);
            return resultado;
        }

        public async Task<ResultadoAnuncioEventoDTO> EnviarProgramado(int anuncioId)
        {
            AnuncioEventoRegistrado anuncio = await anuncios.Leer(anuncioId).ConfigureAwait(false);
            if (anuncio == null || anuncio.EnviadoFecha.HasValue)
            {
                return null;
            }
            DateTime ahora = reloj();
            if (!ReglasAnuncioEvento.DentroDeHorario(ahora))
            {
                // Hangfire lo ha lanzado tarde (servidor parado…): a la próxima franja, nunca de noche
                programar(anuncioId, ReglasAnuncioEvento.ProximaFranja(ahora));
                return null;
            }
            EventoDTO evento = await eventos.LeerEvento(anuncio.EventoId).ConfigureAwait(false);
            if (evento == null || !evento.Activo || evento.Fecha.Date < ahora.Date)
            {
                ElmahHelper.Log(new Exception($"[Anunciar eventos #692] El anuncio {anuncioId} del evento {anuncio.EventoId} no sale: " +
                    "el evento ya no existe, no está activo o ya ha pasado."));
                return null;
            }
            var resultado = new ResultadoAnuncioEventoDTO
            {
                EventoId = anuncio.EventoId,
                AnunciadoFecha = anuncio.Fecha,
                AnunciadoUsuario = anuncio.Usuario,
                Reclamo = anuncio.Reclamo
            };
            await Enviar(evento, anuncio.Reclamo, resultado).ConfigureAwait(false);
            await anuncios.MarcarEnviado(anuncioId, reloj(), resultado.DispositivosNestoApp, resultado.DispositivosTiendas,
                resultado.ClientesTiendas).ConfigureAwait(false);
            resultado.Mensaje = ReglasAnuncioEvento.Mensaje(resultado, ahora);
            return resultado;
        }

        /// <summary>Manda las dos push. Un fallo en una app no deja sin anuncio a la otra (ni un cliente a los demás).</summary>
        private async Task Enviar(EventoDTO evento, string reclamo, ResultadoAnuncioEventoDTO resultado)
        {
            try
            {
                resultado.DispositivosNestoApp = await push.EnviarATodosDeAplicacion(Constantes.Aplicaciones.NESTO_APP,
                    ReglasAnuncioEvento.NotificacionVendedores(evento, reclamo)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Anunciar eventos #692] No se ha podido mandar a NestoApp el evento {evento.Id}: {ex.Message}", ex));
            }

            string empresa = string.IsNullOrWhiteSpace(evento.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : evento.Empresa.Trim();
            List<ClienteAppTiendas> conApp;
            try
            {
                conApp = await anuncios.ClientesConAppTiendas(empresa).ConfigureAwait(false) ?? new List<ClienteAppTiendas>();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Anunciar eventos #692] No se han podido leer los clientes con la app de la tienda: {ex.Message}", ex));
                return;
            }
            ISet<string> deBaja = await ServicioPreferenciasNotificaciones.ClientesDeBaja(preferencias, empresa,
                TiposNotificacionCliente.EVENTOS_Y_CURSOS).ConfigureAwait(false);
            List<string> destinatarios = ReglasAnuncioEvento.Destinatarios(conApp, deBaja, ReglasAnuncioEvento.EsOnline(evento));
            NotificacionPushDTO notificacion = ReglasAnuncioEvento.NotificacionClientes(evento, reclamo);
            foreach (string cliente in destinatarios)
            {
                try
                {
                    int enviados = await push.EnviarACliente(empresa, cliente, notificacion).ConfigureAwait(false);
                    resultado.DispositivosTiendas += enviados;
                    resultado.ClientesTiendas += enviados > 0 ? 1 : 0;
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Anunciar eventos #692] Cliente {cliente}: {ex.Message}", ex));
                }
            }
        }

        private static NestoBusinessException YaAnunciado(DateTime fecha, string usuario)
        {
            string quien = string.IsNullOrWhiteSpace(usuario) ? string.Empty : $" por {usuario.Trim()}";
            return new NestoBusinessException(
                $"Este evento ya se anunció el {fecha:dd/MM/yyyy} a las {fecha:HH:mm}{quien}. Si quiere volver a anunciarlo, use " +
                "«Anunciar otra vez» con un reclamo.",
                new ErrorContext { ErrorCode = ReglasAnuncioEvento.ERROR_YA_ANUNCIADO }.WithData("anunciadoFecha", fecha))
            { StatusCode = HttpStatusCode.Conflict, IsWarning = true };
        }
    }

    /// <summary>NestoAPI#692: el job de Hangfire de los anuncios pedidos fuera de 9:00-21:00.</summary>
    public static class AnunciosEventosJobsService
    {
        [AutomaticRetry(Attempts = 0)]
        public static async Task EnviarProgramado(int anuncioId)
        {
            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    var servicio = new ServicioAnunciosEventos(new ServicioEventos(db), new RepositorioAnunciosEventosSql(db),
                        new ServicioNotificacionesPush(), new RepositorioPreferenciasNotificacionesSql(db));
                    _ = await servicio.EnviarProgramado(anuncioId).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Anunciar eventos #692] Error en el anuncio programado {anuncioId}: {ex.Message}", ex));
                throw;
            }
        }
    }
}
