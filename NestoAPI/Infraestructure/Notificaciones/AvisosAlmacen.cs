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
    /// termina, la API manda al grupo solo «haCambiado(grupo, dispositivo)» (sin datos), a todos menos a la PDA que lo ha hecho
    /// (la que se actualiza en local con la respuesta de su POST): si está sola, no se manda nada. La PDA, al recibirlo, pide la
    /// recogida (o el packing) ligera. Misma ruta y mismo JWT (en la query string, access_token) que <see cref="AvisosHub"/>.
    /// </summary>
    [Authorize]
    public class AlmacenHub : Hub
    {
        /// <summary>La PDA abre un recorrido o un packing (sin decir su dispositivo: versiones sin él). False si el grupo no vale.</summary>
        public Task<bool> Entrar(string grupo) => Entrar(grupo, null);

        /// <summary>
        /// La PDA abre un recorrido o un packing y dice qué dispositivo es (el mismo que manda en sus escaneos y en la cabecera
        /// X-Dispositivo): así, si un POST llega sin la cabecera de su conexión, no se le avisa de lo suyo. False si el grupo no vale.
        /// </summary>
        public async Task<bool> Entrar(string grupo, string dispositivo)
        {
            string limpio = AvisosAlmacen.Normalizar(grupo);
            string usuario = Context.User?.Identity?.Name;
            if (limpio == null || string.IsNullOrWhiteSpace(usuario))
            {
                return false;
            }
            await Groups.Add(Context.ConnectionId, limpio).ConfigureAwait(false);
            ConexionesAlmacen.Entrar(Context.ConnectionId, usuario, dispositivo, limpio);
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
    /// NestoAPI#646: qué conexión de Ariadna está en qué grupo, de qué usuario y de qué dispositivo, para no avisar a la PDA que ha
    /// hecho el cambio. En memoria: se vacía al reciclar la API; las PDA vuelven a entrar en su grupo al reconectar.
    /// </summary>
    public static class ConexionesAlmacen
    {
        private sealed class Conexion
        {
            public string Usuario { get; set; }
            public string Dispositivo { get; set; }
            public ConcurrentDictionary<string, bool> Grupos { get; } = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly ConcurrentDictionary<string, Conexion> conexiones = new ConcurrentDictionary<string, Conexion>();

        public static void Entrar(string conexion, string usuario, string dispositivo, string grupo)
        {
            if (string.IsNullOrWhiteSpace(conexion) || string.IsNullOrWhiteSpace(grupo))
            {
                return;
            }
            Conexion datos = conexiones.GetOrAdd(conexion, _ => new Conexion());
            datos.Usuario = usuario?.Trim();
            if (!string.IsNullOrWhiteSpace(dispositivo))
            {
                datos.Dispositivo = dispositivo.Trim();
            }
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

        /// <summary>
        /// Las conexiones del grupo a las que NO se avisa: la que manda la cabecera X-Conexion-SignalR; si no llega (versión vieja,
        /// o la PDA sin SignalR conectado), las del mismo dispositivo; y si tampoco se sabe el dispositivo, las del mismo usuario.
        /// </summary>
        public static List<string> AExcluir(string grupo, string usuario, string conexionEmisora, string dispositivoEmisor)
        {
            if (!string.IsNullOrWhiteSpace(conexionEmisora))
            {
                return new List<string> { conexionEmisora.Trim() };
            }
            string dispositivo = dispositivoEmisor?.Trim();
            string quien = usuario?.Trim();
            return conexiones
                .Where(c => c.Value.Grupos.ContainsKey(grupo ?? string.Empty))
                .Where(c => !string.IsNullOrEmpty(dispositivo)
                    ? string.Equals(c.Value.Dispositivo, dispositivo, StringComparison.OrdinalIgnoreCase)
                    : !string.IsNullOrEmpty(quien) && string.Equals(c.Value.Usuario, quien, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Key)
                .ToList();
        }

        /// <summary>Queda alguien en el grupo fuera de <paramref name="excluidas"/> (si no, no se manda nada).</summary>
        public static bool HayOtros(string grupo, IEnumerable<string> excluidas)
        {
            var fuera = new HashSet<string>(excluidas ?? Enumerable.Empty<string>());
            return conexiones.Any(c => c.Value.Grupos.ContainsKey(grupo ?? string.Empty) && !fuera.Contains(c.Key));
        }

        internal static void Vaciar() => conexiones.Clear();
    }

    /// <summary>NestoAPI#646: avisar a las demás PDA de un documento de que ha cambiado. Nunca lanza.</summary>
    public interface IAvisosAlmacen
    {
        /// <param name="grupo">El del documento (<see cref="AvisosAlmacen.GrupoRecogida"/> o <see cref="AvisosAlmacen.GrupoPacking"/>).</param>
        /// <param name="usuario">El que ha hecho el cambio.</param>
        /// <param name="conexionEmisora">La conexión de SignalR de la PDA que lo ha hecho (cabecera X-Conexion-SignalR). Null si no llega.</param>
        /// <param name="dispositivoEmisor">El dispositivo que lo ha hecho (cabecera X-Dispositivo o el de la petición). Null si no se sabe.</param>
        void HaCambiado(string grupo, string usuario, string conexionEmisora, string dispositivoEmisor);
    }

    /// <summary>Los nombres de los grupos (los mismos en la API y en Ariadna) y las cabeceras.</summary>
    public static class AvisosAlmacen
    {
        /// <summary>La conexión de SignalR de la PDA que hace el POST (para no avisarle de lo suyo).</summary>
        public const string CABECERA_CONEXION = "X-Conexion-SignalR";
        /// <summary>El dispositivo que hace el POST (si no llega la conexión, se excluyen las de este dispositivo).</summary>
        public const string CABECERA_DISPOSITIVO = "X-Dispositivo";

        private static readonly Regex FORMATO = new Regex(@"^(Recogida:(PICK|REPO):\d{1,9}|Packing:\d{1,9}|recepcion:[A-Z]{4}:[A-Z0-9._-]{1,20}:[A-Z0-9]{1,3})$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>«Recogida:PICK:99633», «Recogida:REPO:80905».</summary>
        public static string GrupoRecogida(string tipo, int numero) => $"Recogida:{(tipo ?? string.Empty).Trim().ToUpperInvariant()}:{numero}";

        /// <summary>«Packing:99633»: todo el picking (la pantalla de packing enseña todas sus entregas).</summary>
        public static string GrupoPacking(int picking) => $"Packing:{picking}";

        /// <summary>
        /// Ariadna#51 (corte 2): «recepcion:REPO:81119:ALG», «recepcion:COMP:65:ALG». Lo leído de una recepción en curso, compartido
        /// entre Nesto y Ariadna: quien apunta, quita, descarta o termina avisa a los demás equipos de esa recepción en ese almacén.
        /// </summary>
        public static string GrupoRecepcion(string tipo, string documento, string almacen) =>
            $"recepcion:{(tipo ?? string.Empty).Trim().ToUpperInvariant()}:{(documento ?? string.Empty).Trim().ToUpperInvariant()}:{(almacen ?? string.Empty).Trim().ToUpperInvariant()}";

        /// <summary>El grupo bien escrito, o null si no es de los que hay.</summary>
        public static string Normalizar(string grupo)
        {
            string limpio = grupo?.Trim();
            if (string.IsNullOrEmpty(limpio) || !FORMATO.IsMatch(limpio))
            {
                return null;
            }
            string[] partes = limpio.Split(':');
            if (partes.Length == 4)
            {
                return GrupoRecepcion(partes[1], partes[2], partes[3]);
            }
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

        public void HaCambiado(string grupo, string usuario, string conexionEmisora, string dispositivoEmisor)
        {
            try
            {
                string limpio = AvisosAlmacen.Normalizar(grupo);
                if (limpio == null)
                {
                    return;
                }
                List<string> excluidas = ConexionesAlmacen.AExcluir(limpio, usuario, conexionEmisora, dispositivoEmisor);
                if (!ConexionesAlmacen.HayOtros(limpio, excluidas))
                {
                    return; // sola, o nadie mirando: nada que mandar
                }
                contexto().Clients.Group(limpio, excluidas.ToArray()).haCambiado(limpio, dispositivoEmisor?.Trim() ?? string.Empty);
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
