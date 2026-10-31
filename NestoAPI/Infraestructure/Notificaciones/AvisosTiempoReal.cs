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
                _ = Groups.Add(Context.ConnectionId, AvisosTiempoReal.GrupoDe(usuario));
                UsuariosConectadosNesto.Registrar(Context.ConnectionId, usuario);
            }
            return base.OnConnected();
        }

        public override Task OnReconnected()
        {
            string usuario = Context.User?.Identity?.Name;
            if (!string.IsNullOrWhiteSpace(usuario))
            {
                _ = Groups.Add(Context.ConnectionId, AvisosTiempoReal.GrupoDe(usuario));
                UsuariosConectadosNesto.Registrar(Context.ConnectionId, usuario);
            }
            return base.OnReconnected();
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
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> conexiones =
            new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

        public static void Registrar(string conexion, string usuario)
        {
            if (!string.IsNullOrWhiteSpace(conexion) && !string.IsNullOrWhiteSpace(usuario))
            {
                conexiones[conexion] = usuario.Trim();
            }
        }

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
            var vivos = new HashSet<string>(conexiones.Values.Select(SinDominio), StringComparer.OrdinalIgnoreCase);
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
            return buscado.Length == 0 ? 0 : conexiones.Values.Count(u => string.Equals(SinDominio(u), buscado, StringComparison.OrdinalIgnoreCase));
        }

        internal static string SinDominio(string usuario)
        {
            string limpio = (usuario ?? string.Empty).Trim();
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 ? limpio.Substring(barra + 1) : limpio;
        }

        /// <summary>Los usuarios distintos con al menos un Nesto conectado.</summary>
        public static List<string> Usuarios()
            => conexiones.Values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(u => u, StringComparer.OrdinalIgnoreCase).ToList();

        internal static void Vaciar() => conexiones.Clear();
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

        public static string GrupoDe(string usuario) => "usuario:" + (usuario ?? string.Empty).Trim().ToUpperInvariant();

        public void HayNotificacionesNuevas(IEnumerable<string> usuarios)
            => ACadaUno(usuarios, grupo => grupo.hayNotificacionesNuevas());

        public void HayNotificacionUrgente(IEnumerable<string> usuarios)
            => ACadaUno(usuarios, grupo => grupo.hayNotificacionUrgente());

        public void NotificacionResuelta(IEnumerable<string> usuarios, NotificacionResueltaDTO resuelta)
        {
            if (resuelta != null)
            {
                ACadaUno(usuarios, grupo => grupo.notificacionResuelta(resuelta));
            }
        }

        private static void ACadaUno(IEnumerable<string> usuarios, Action<dynamic> avisar)
        {
            try
            {
                IHubContext contexto = GlobalHost.ConnectionManager.GetHubContext<AvisosHub>();
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
