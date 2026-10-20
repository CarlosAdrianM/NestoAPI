using Microsoft.AspNet.SignalR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Notificaciones
{
    /// <summary>
    /// NestoAPI#646 (Carlos 20/10/26): varias PDA en la misma salida o el mismo packing, SIN sondeo («si alguien se deja la
    /// pantalla abierta está consumiendo recursos… la mayoría de las veces prepara el picking uno solo»). Cada Ariadna entra al
    /// abrir el recorrido o el packing en el grupo de ese documento (<see cref="AvisosAlmacen.GrupoRecogida"/>,
    /// <see cref="AvisosAlmacen.GrupoPacking"/>) y sale al cerrarlo. Cuando alguien lee, da una falta, corrige, cierra un bulto o
    /// termina, la API manda al grupo solo «haCambiado» (sin datos), a todos menos al usuario que lo ha hecho: si está solo, no
    /// se manda nada. La PDA, al recibirlo, pide la recogida (o el packing) ligera. Misma ruta y mismo JWT (en la query string,
    /// access_token) que <see cref="AvisosHub"/>.
    /// </summary>
    [Authorize]
    public class AlmacenHub : Hub
    {
        /// <summary>La PDA abre un recorrido o un packing. False si el grupo no es de los que hay.</summary>
        public async Task<bool> Entrar(string grupo)
        {
            string limpio = AvisosAlmacen.Normalizar(grupo);
            string usuario = Context.User?.Identity?.Name;
            if (limpio == null || string.IsNullOrWhiteSpace(usuario))
            {
                return false;
            }
            await Groups.Add(Context.ConnectionId, limpio).ConfigureAwait(false);
            ConexionesAlmacen.Entrar(Context.ConnectionId, usuario, limpio);
            return true;
        }

        /// <summary>La PDA cierra la pantalla.</summary>
        public async Task Salir(string grupo)
        {
            string limpio = AvisosAlmacen.Normalizar(grupo);
            if (limpio == null)
            {
                return;
            }
            ConexionesAlmacen.Salir(Context.ConnectionId, limpio);
            await Groups.Remove(Context.ConnectionId, limpio).ConfigureAwait(false);
        }

        public override Task OnDisconnected(bool stopCalled)
        {
            ConexionesAlmacen.Quitar(Context.ConnectionId);
            return base.OnDisconnected(stopCalled);
        }
    }

    /// <summary>
    /// NestoAPI#646: qué conexión de Ariadna está en qué grupo y de qué usuario es, para no avisar al que ha hecho el cambio.
    /// En memoria: se vacía al reciclar la API; las PDA se reconectan solas y vuelven a entrar en su grupo.
    /// </summary>
    public static class ConexionesAlmacen
    {
        private sealed class Conexion
        {
            public string Usuario { get; set; }
            public ConcurrentDictionary<string, bool> Grupos { get; } = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly ConcurrentDictionary<string, Conexion> conexiones = new ConcurrentDictionary<string, Conexion>();

        public static void Entrar(string conexion, string usuario, string grupo)
        {
            if (string.IsNullOrWhiteSpace(conexion) || string.IsNullOrWhiteSpace(grupo))
            {
                return;
            }
            Conexion datos = conexiones.GetOrAdd(conexion, _ => new Conexion());
            datos.Usuario = usuario?.Trim();
            datos.Grupos[grupo] = true;
        }

        public static void Salir(string conexion, string grupo)
        {
            if (!string.IsNullOrWhiteSpace(conexion) && conexiones.TryGetValue(conexion, out Conexion datos))
            {
                _ = datos.Grupos.TryRemove(grupo, out _);
            }
        }

        public static void Quitar(string conexion)
        {
            if (!string.IsNullOrWhiteSpace(conexion))
            {
                _ = conexiones.TryRemove(conexion, out _);
            }
        }

        /// <summary>Las conexiones del grupo que son del usuario que ha hecho el cambio: a esas no se les avisa.</summary>
        public static List<string> DelUsuarioEnElGrupo(string grupo, string usuario)
        {
            string quien = usuario?.Trim();
            return conexiones
                .Where(c => c.Value.Grupos.ContainsKey(grupo ?? string.Empty)
                    && !string.IsNullOrEmpty(quien) && string.Equals(c.Value.Usuario, quien, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Key)
                .ToList();
        }

        /// <summary>Hay alguien de OTRO usuario en el grupo (si no, no se manda nada).</summary>
        public static bool HayOtros(string grupo, string usuario)
        {
            string quien = usuario?.Trim();
            return conexiones.Any(c => c.Value.Grupos.ContainsKey(grupo ?? string.Empty)
                && !string.Equals(c.Value.Usuario, quien, StringComparison.OrdinalIgnoreCase));
        }

        internal static void Vaciar() => conexiones.Clear();
    }

    /// <summary>NestoAPI#646: avisar a las demás PDA de un documento de que ha cambiado. Nunca lanza.</summary>
    public interface IAvisosAlmacen
    {
        /// <param name="grupo">El del documento (<see cref="AvisosAlmacen.GrupoRecogida"/> o <see cref="AvisosAlmacen.GrupoPacking"/>).</param>
        /// <param name="usuario">El que ha hecho el cambio: a sus conexiones no se les avisa.</param>
        void HaCambiado(string grupo, string usuario);
    }

    /// <summary>Los nombres de los grupos (los mismos en la API y en Ariadna).</summary>
    public static class AvisosAlmacen
    {
        private static readonly Regex FORMATO = new Regex(@"^(Recogida:(PICK|REPO):\d{1,9}|Packing:\d{1,9})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>«Recogida:PICK:99633», «Recogida:REPO:80905».</summary>
        public static string GrupoRecogida(string tipo, int numero) => $"Recogida:{(tipo ?? string.Empty).Trim().ToUpperInvariant()}:{numero}";

        /// <summary>«Packing:99633»: todo el picking (la pantalla de packing enseña todas sus entregas).</summary>
        public static string GrupoPacking(int picking) => $"Packing:{picking}";

        /// <summary>El grupo bien escrito, o null si no es de los que hay.</summary>
        public static string Normalizar(string grupo)
        {
            string limpio = grupo?.Trim();
            if (string.IsNullOrEmpty(limpio) || !FORMATO.IsMatch(limpio))
            {
                return null;
            }
            string[] partes = limpio.Split(':');
            return partes.Length == 3 ? GrupoRecogida(partes[1], int.Parse(partes[2])) : GrupoPacking(int.Parse(partes[1]));
        }
    }

    public class AvisosAlmacenSignalR : IAvisosAlmacen
    {
        private readonly Func<IHubContext> contexto;

        public AvisosAlmacenSignalR() : this(() => GlobalHost.ConnectionManager.GetHubContext<AlmacenHub>())
        {
        }

        internal AvisosAlmacenSignalR(Func<IHubContext> contexto)
        {
            this.contexto = contexto;
        }

        public void HaCambiado(string grupo, string usuario)
        {
            try
            {
                string limpio = AvisosAlmacen.Normalizar(grupo);
                if (limpio == null || !ConexionesAlmacen.HayOtros(limpio, usuario))
                {
                    return; // solo, o nadie mirando: nada que mandar
                }
                IList<string> excluidas = ConexionesAlmacen.DelUsuarioEnElGrupo(limpio, usuario);
                contexto().Clients.Group(limpio, excluidas.ToArray()).haCambiado(limpio);
            }
            catch (Exception ex)
            {
                // El aviso es un extra: lo leído ya está guardado y la otra PDA lo verá al refrescar
                try
                {
                    ElmahHelper.Log(new Exception("[SignalR almacén] No se pudo avisar de un cambio: " + ex.Message, ex));
                }
                catch
                {
                    // nada
                }
            }
        }
    }
}
