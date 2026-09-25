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
            return base.OnDisconnected(stopCalled);
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

        /// <summary>Los usuarios distintos con al menos un Nesto conectado.</summary>
        public static List<string> Usuarios()
            => conexiones.Values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(u => u, StringComparer.OrdinalIgnoreCase).ToList();

        internal static void Vaciar() => conexiones.Clear();
    }

    public interface IAvisosTiempoReal
    {
        /// <summary>Avisa a los Nesto conectados de esos usuarios de que tienen algo nuevo. Nunca lanza.</summary>
        void HayNotificacionesNuevas(IEnumerable<string> usuarios);
    }

    public class AvisosTiempoReal : IAvisosTiempoReal
    {
        public const string RUTA = "/signalr";

        public static string GrupoDe(string usuario) => "usuario:" + (usuario ?? string.Empty).Trim().ToUpperInvariant();

        public void HayNotificacionesNuevas(IEnumerable<string> usuarios)
        {
            try
            {
                IHubContext contexto = GlobalHost.ConnectionManager.GetHubContext<AvisosHub>();
                foreach (string usuario in (usuarios ?? Enumerable.Empty<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    contexto.Clients.Group(GrupoDe(usuario)).hayNotificacionesNuevas();
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
