using Microsoft.AspNet.SignalR;
using Microsoft.Owin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Notificaciones
{
    /// <summary>
    /// NestoAPI#536: avisos en tiempo real para Nesto (WPF) por SignalR, en vez de que cada puesto
    /// pregunte a la API cada poco (#517: un sondeo frecuente de decenas de puestos tumbó RDS2016).
    /// Cada Nesto se conecta a este hub con su JWT y queda en el grupo de su usuario. Cuando llega algo
    /// a su buzón (Nesto#477) se le manda SOLO el aviso «hayNotificacionesNuevas»; el contenido lo pide
    /// él al buzón como siempre.
    /// </summary>
    [Authorize]
    public class AvisosHub : Hub
    {
        public override Task OnConnected()
        {
            string usuario = Context.User?.Identity?.Name;
            if (!string.IsNullOrWhiteSpace(usuario))
            {
                foreach (string grupo in AvisosTiempoReal.GruposDe(usuario))
                {
                    _ = Groups.Add(Context.ConnectionId, grupo);
                }
                UsuariosConectadosNesto.Registrar(Context.ConnectionId, usuario, VersionNesto());
            }
            return base.OnConnected();
        }

        public override Task OnReconnected()
        {
            string usuario = Context.User?.Identity?.Name;
            if (!string.IsNullOrWhiteSpace(usuario))
            {
                foreach (string grupo in AvisosTiempoReal.GruposDe(usuario))
                {
                    _ = Groups.Add(Context.ConnectionId, grupo);
                }
                UsuariosConectadosNesto.Registrar(Context.ConnectionId, usuario, VersionNesto());
            }
            return base.OnReconnected();
        }

        /// <summary>
        /// NestoAPI#834: la versión de Nesto, si la manda en la cadena de conexión («version»). Hoy (Nesto 1.10.56) no la manda:
        /// solo pasa access_token, así que es null hasta que Nesto la añada.
        /// </summary>
        private string VersionNesto()
        {
            try
            {
                return Context.QueryString?[UsuariosConectadosNesto.PARAMETRO_VERSION];
            }
            catch
            {
                return null;
            }
        }

        public override Task OnDisconnected(bool stopCalled)
        {
            UsuariosConectadosNesto.Quitar(Context.ConnectionId);
            ConexionesEscanerRemoto.Global.Quitar(Context.ConnectionId);
            return base.OnDisconnected(stopCalled);
        }

        /// <summary>
        /// NestoAPI#768 (escáner remoto): Nesto se presenta al conectar (con <paramref name="conFoco"/> = si tiene una ventana
        /// activa) y avisa cada vez que se activa su ventana. Solo las conexiones que lo han llamado reciben «lecturaEscanerRemoto»
        /// (lo leído con Ariadna por el MISMO usuario) y, si hay varias, la del foco más reciente. False sin usuario o con un token de
        /// cliente (TiendasNuevaVision).
        /// </summary>
        public bool ActividadEscanerRemoto(string equipo, bool conFoco)
        {
            string usuario = Context.User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(usuario) || EscanerRemotoPermisos.EsCliente(Context.User))
            {
                return false;
            }
            ConexionesEscanerRemoto.Global.Actividad(Context.ConnectionId, usuario, equipo, conFoco, DateTime.UtcNow);
            return true;
        }
    }

    /// <summary>
    /// Carlos (25/09/26): quién tiene Nesto abierto ahora mismo (cada Nesto se conecta al hub al arrancar). Sirve
    /// para avisar de una versión nueva solo a quien tiene que cerrar y volver a abrir: los que no lo tienen
    /// abierto se actualizan solos al abrirlo. En memoria: se vacía al reciclar la API y se rellena en cuanto
    /// los Nesto se reconectan (lo hacen solos en unos segundos).
    /// </summary>
    public static class UsuariosConectadosNesto
    {
        /// <summary>NestoAPI#834: el parámetro de la cadena de conexión del hub con la versión de Nesto (si la manda).</summary>
        public const string PARAMETRO_VERSION = "version";

        private class Conexion
        {
            public string Usuario;
            public string Version;
            public DateTime Desde;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Conexion> conexiones =
            new System.Collections.Concurrent.ConcurrentDictionary<string, Conexion>();

        /// <summary>
        /// Alta (o reconexión) de un Nesto. NestoAPI#834: con su versión si la manda y la hora del alta; en una reconexión de la misma
        /// conexión se conserva la hora del alta.
        /// </summary>
        public static void Registrar(string conexion, string usuario, string version = null, DateTime? ahora = null)
        {
            if (!string.IsNullOrWhiteSpace(conexion) && !string.IsNullOrWhiteSpace(usuario))
            {
                DateTime momento = ahora ?? DateTime.Now;
                string limpia = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
                _ = conexiones.AddOrUpdate(conexion,
                    _ => new Conexion { Usuario = usuario.Trim(), Version = limpia, Desde = momento },
                    (_, anterior) => new Conexion { Usuario = usuario.Trim(), Version = limpia ?? anterior.Version, Desde = anterior.Desde });
            }
        }

        /// <summary>
        /// NestoAPI#834: cada Nesto conectado (una fila por conexión: quien tiene dos Nesto abiertos sale dos veces, quizá con versiones
        /// distintas), con su versión (null si no la manda) y desde cuándo, por usuario y hora.
        /// </summary>
        public static List<ConexionNestoDTO> Conexiones()
            => conexiones.Values
                .Select(c => new ConexionNestoDTO { Usuario = c.Usuario, Version = c.Version, Desde = c.Desde })
                .OrderBy(c => c.Usuario, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Desde)
                .ToList();

        public static void Quitar(string conexion)
        {
            if (!string.IsNullOrWhiteSpace(conexion))
            {
                _ = conexiones.TryRemove(conexion, out _);
            }
        }

        /// <summary>
        /// NestoAPI#775: de esos usuarios (con o sin dominio, sin distinguir mayúsculas), los que tienen ahora al menos un Nesto
        /// conectado, sin dominio y sin repetidos (una persona con dos Nesto abiertos cuenta una vez).
        /// </summary>
        public static List<string> Conectados(IEnumerable<string> usuarios)
        {
            var vivos = new HashSet<string>(conexiones.Values.Select(c => SinDominio(c.Usuario)), StringComparer.OrdinalIgnoreCase);
            return (usuarios ?? Enumerable.Empty<string>())
                .Select(SinDominio)
                .Where(u => u.Length > 0 && vivos.Contains(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(u => u, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>NestoAPI#775: cuántas conexiones (Nesto abiertos) tiene ese usuario.</summary>
        public static int ConexionesDe(string usuario)
        {
            string buscado = SinDominio(usuario);
            return buscado.Length == 0 ? 0 : conexiones.Values.Count(c => string.Equals(SinDominio(c.Usuario), buscado, StringComparison.OrdinalIgnoreCase));
        }

        internal static string SinDominio(string usuario)
        {
            string limpio = (usuario ?? string.Empty).Trim();
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 ? limpio.Substring(barra + 1) : limpio;
        }

        /// <summary>Los usuarios distintos con al menos un Nesto conectado.</summary>
        public static List<string> Usuarios()
            => conexiones.Values.Select(c => c.Usuario).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(u => u, StringComparer.OrdinalIgnoreCase).ToList();

        internal static void Vaciar() => conexiones.Clear();
    }

    /// <summary>NestoAPI#834: un Nesto conectado (GET api/Notificaciones/ConectadosNesto).</summary>
    public class ConexionNestoDTO
    {
        public string Usuario { get; set; }
        /// <summary>Null si Nesto no la manda al conectar.</summary>
        public string Version { get; set; }
        public DateTime Desde { get; set; }
    }

    public interface IAvisosTiempoReal
    {
        /// <summary>Avisa a los Nesto conectados de esos usuarios de que tienen algo nuevo. Nunca lanza.</summary>
        void HayNotificacionesNuevas(IEnumerable<string> usuarios);

        /// <summary>
        /// NestoAPI#775: «hayNotificacionUrgente» a los Nesto de esos usuarios: piden GET api/Notificaciones/Urgentes y abren la ventana
        /// roja. Va además de «hayNotificacionesNuevas» (la campana se refresca igual). Nunca lanza.
        /// </summary>
        void HayNotificacionUrgente(IEnumerable<string> usuarios);

        /// <summary>
        /// NestoAPI#775: «notificacionResuelta» {Tipo, Referencia, Respondio, Resultado}: alguien ha contestado una urgente y se cierra en
        /// todos los Nesto que la tengan abierta. Nunca lanza.
        /// </summary>
        void NotificacionResuelta(IEnumerable<string> usuarios, NotificacionResueltaDTO resuelta);
    }

    public class AvisosTiempoReal : IAvisosTiempoReal
    {
        public const string RUTA = "/signalr";

        private readonly Func<IHubContext> hubAvisos;
        private readonly Func<IHubContext> hubAlmacen;

        public AvisosTiempoReal() : this(() => GlobalHost.ConnectionManager.GetHubContext<AvisosHub>(),
            () => GlobalHost.ConnectionManager.GetHubContext<AlmacenHub>())
        {
        }

        internal AvisosTiempoReal(Func<IHubContext> hubAvisos, Func<IHubContext> hubAlmacen)
        {
            this.hubAvisos = hubAvisos;
            this.hubAlmacen = hubAlmacen;
        }

        public static string GrupoDe(string usuario) => "usuario:" + (usuario ?? string.Empty).Trim().ToUpperInvariant();

        /// <summary>
        /// NestoAPI#822: los grupos en los que entra una conexión: el del nombre del token y el de la persona
        /// (<see cref="UsuarioAuditoriaHelper.Persona"/>). Así «Carlos» (Ariadna, NestoApp) y «NUEVAVISION\Carlos» (Nesto)
        /// reciben los avisos de su buzón, que es uno solo, los manden con el nombre que los manden. Sin repetidos.
        /// </summary>
        public static List<string> GruposDe(string usuario)
        {
            var grupos = new List<string>();
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return grupos;
            }
            grupos.Add(GrupoDe(usuario));
            string persona = UsuarioAuditoriaHelper.Persona(usuario.Trim());
            if (!string.IsNullOrWhiteSpace(persona) && !grupos.Contains(GrupoDe(persona)))
            {
                grupos.Add(GrupoDe(persona));
            }
            return grupos;
        }

        /// <summary>
        /// A los Nesto (AvisosHub) y, NestoAPI#822, también a las Ariadna (AlmacenHub) de esos usuarios: «hayNotificacionesNuevas»
        /// sin datos y la campana se refresca (Ariadna#97; así Ariadna puede quitar su reloj). Nunca lanza.
        /// </summary>
        public void HayNotificacionesNuevas(IEnumerable<string> usuarios)
        {
            List<string> lista = (usuarios ?? Enumerable.Empty<string>()).ToList();
            ACadaUno(hubAvisos, lista, grupo => grupo.hayNotificacionesNuevas());
            ACadaUno(hubAlmacen, lista, grupo => grupo.hayNotificacionesNuevas());
        }

        public void HayNotificacionUrgente(IEnumerable<string> usuarios)
            => ACadaUno(hubAvisos, usuarios, grupo => grupo.hayNotificacionUrgente());

        public void NotificacionResuelta(IEnumerable<string> usuarios, NotificacionResueltaDTO resuelta)
        {
            if (resuelta != null)
            {
                ACadaUno(hubAvisos, usuarios, grupo => grupo.notificacionResuelta(resuelta));
            }
        }

        private static void ACadaUno(Func<IHubContext> hub, IEnumerable<string> usuarios, Action<dynamic> avisar)
        {
            try
            {
                IHubContext contexto = hub();
                foreach (string usuario in (usuarios ?? Enumerable.Empty<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    avisar(contexto.Clients.Group(GrupoDe(usuario)));
                }
            }
            catch (Exception ex)
            {
                // El aviso es un extra: el buzón ya está guardado y Nesto lo verá en el siguiente refresco.
                try
                {
                    ElmahHelper.Log(new Exception("[SignalR] No se pudo avisar en tiempo real: " + ex.Message, ex));
                }
                catch
                {
                    // nada
                }
            }
        }

        /// <summary>
        /// SignalR no manda el JWT en la cabecera Authorization al abrir la conexión: lo manda en la query
        /// string (access_token). Solo en la ruta de SignalR y solo si no venía ya en la cabecera; en el
        /// resto de rutas no cambia nada. Pura para testear.
        /// </summary>
        public static string TokenDeLaPeticion(PathString ruta, string tokenDeCabecera, string tokenDeQuery)
        {
            if (!string.IsNullOrEmpty(tokenDeCabecera))
            {
                return tokenDeCabecera;
            }
            return ruta.StartsWithSegments(new PathString(RUTA)) && !string.IsNullOrWhiteSpace(tokenDeQuery)
                ? tokenDeQuery
                : tokenDeCabecera;
        }
    }
}
