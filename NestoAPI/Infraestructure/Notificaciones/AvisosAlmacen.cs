using Microsoft.AspNet.SignalR;
using System;
using System.Collections.Concurrent;
using NestoAPI.Models.PreparacionAlmacen;
using System.Collections.Generic;
using System.Configuration;
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

        /// <summary>
        /// NestoAPI#710: lo que se acaba de grabar en PreparacionEscaneos para ese grupo, CON SUS DATOS: «lectura(grupo, dispositivo,
        /// lectura)» si es una y «lecturas(grupo, dispositivo, [..])» si es un lote. Según <see cref="AvisosAlmacen.CLAVE_AVISOS_CON_DATOS"/>,
        /// detrás va también el «haCambiado» de siempre (las versiones de Ariadna que solo escuchan ese). Mismas exclusiones que
        /// <see cref="HaCambiado"/>. Nunca lanza.
        /// </summary>
        void Lecturas(string grupo, string usuario, string conexionEmisora, string dispositivoEmisor, IReadOnlyList<LecturaAlmacenAvisoDTO> lecturas);

        /// <summary>
        /// NestoAPI#710: una parada corregida (quitada la falta o lo cogido): «correccion(grupo, dispositivo, correccion)» y, según
        /// <see cref="AvisosAlmacen.CLAVE_AVISOS_CON_DATOS"/>, el «haCambiado» de siempre. Nunca lanza.
        /// </summary>
        void Correccion(string grupo, string usuario, string conexionEmisora, string dispositivoEmisor, CorreccionAlmacenAvisoDTO correccion);
    }

    /// <summary>
    /// NestoAPI#710: qué se manda por una lectura o una corrección (Web.config, <see cref="AvisosAlmacen.CLAVE_AVISOS_CON_DATOS"/>).
    /// Terminar, cambiar de hueco, la foto de un bulto o anular lecturas mandan SIEMPRE «haCambiado» (ahí sí conviene releerlo todo).
    /// </summary>
    public enum ModoAvisosConDatos
    {
        /// <summary>«Ambos» (por defecto, también sin la clave o con un valor que no se entiende): los eventos con datos y, detrás, «haCambiado».</summary>
        Ambos,
        /// <summary>«SoloDatos»: solo los eventos con datos. Cuando todas las PDA tengan Ariadna 2.19 o posterior.</summary>
        SoloDatos,
        /// <summary>«SoloHaCambiado»: como antes de NestoAPI#710 (freno de mano si los datos dieran problemas).</summary>
        SoloHaCambiado
    }

    /// <summary>Los nombres de los grupos (los mismos en la API y en Ariadna) y las cabeceras.</summary>
    public static class AvisosAlmacen
    {
        /// <summary>La conexión de SignalR de la PDA que hace el POST (para no avisarle de lo suyo).</summary>
        public const string CABECERA_CONEXION = "X-Conexion-SignalR";
        /// <summary>El dispositivo que hace el POST (si no llega la conexión, se excluyen las de este dispositivo).</summary>
        public const string CABECERA_DISPOSITIVO = "X-Dispositivo";

        /// <summary>NestoAPI#710: la clave del Web.config con el <see cref="ModoAvisosConDatos"/> (Ambos, SoloDatos o SoloHaCambiado).</summary>
        public const string CLAVE_AVISOS_CON_DATOS = "Almacen:AvisosConDatos";

        /// <summary>NestoAPI#710: el modo de un valor del Web.config. Vacío o desconocido: <see cref="ModoAvisosConDatos.Ambos"/>.</summary>
        public static ModoAvisosConDatos LeerModo(string valor)
        {
            return Enum.TryParse((valor ?? string.Empty).Trim(), true, out ModoAvisosConDatos modo) && Enum.IsDefined(typeof(ModoAvisosConDatos), modo)
                ? modo
                : ModoAvisosConDatos.Ambos;
        }

        private static readonly Regex FORMATO = new Regex(@"^(Recogida:(PICK|REPO):\d{1,9}|Packing:\d{1,9}|recepcion:[A-Z]{4}:[A-Z0-9._-]{1,20}:[A-Z0-9]{1,3}|Reparto|PagoTPV:\d{1,9}|AutorizacionReembolso:\d{1,9})$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// NestoAPI#759: «Reparto», el de la pantalla de Reparto de Ariadna (todas las PDA del reparto propio: la lista es de
        /// todos, filtrada por repartidor en GET Envios). Evento «repartoCambiado(RepartoCambiadoDTO)».
        /// </summary>
        public const string GRUPO_REPARTO = "Reparto";

        /// <summary>«Recogida:PICK:99633», «Recogida:REPO:80905».</summary>
        public static string GrupoRecogida(string tipo, int numero) => $"Recogida:{(tipo ?? string.Empty).Trim().ToUpperInvariant()}:{numero}";

        /// <summary>«Packing:99633»: todo el picking (la pantalla de packing enseña todas sus entregas).</summary>
        public static string GrupoPacking(int picking) => $"Packing:{picking}";

        /// <summary>
        /// NestoAPI#773: «PagoTPV:123», el de la PDA que espera el pago de un enlace con tarjeta del reparto (entra al generarlo y sale
        /// al pagarse o al dejarlo). Evento «pagoCompletado(PagoCompletadoRepartoDTO)».
        /// </summary>
        public static string GrupoPagoTPV(int pagoTPVId) => $"PagoTPV:{pagoTPVId}";

        /// <summary>NestoAPI#773: «AutorizacionReembolso:12». Evento «autorizacionRespondida(AutorizacionReembolsoDTO)».</summary>
        public static string GrupoAutorizacion(int autorizacionId) => $"AutorizacionReembolso:{autorizacionId}";

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
            if (string.Equals(limpio, GRUPO_REPARTO, StringComparison.OrdinalIgnoreCase))
            {
                return GRUPO_REPARTO;
            }
            string[] partes = limpio.Split(':');
            if (partes.Length == 4)
            {
                return GrupoRecepcion(partes[1], partes[2], partes[3]);
            }
            if (partes.Length == 2 && string.Equals(partes[0], "PagoTPV", StringComparison.OrdinalIgnoreCase))
            {
                return GrupoPagoTPV(int.Parse(partes[1]));
            }
            if (partes.Length == 2 && string.Equals(partes[0], "AutorizacionReembolso", StringComparison.OrdinalIgnoreCase))
            {
                return GrupoAutorizacion(int.Parse(partes[1]));
            }
            return partes.Length == 3 ? GrupoRecogida(partes[1], int.Parse(partes[2])) : GrupoPacking(int.Parse(partes[1]));
        }
    }

    /// <summary>
    /// NestoAPI#646 y #710: los avisos por SignalR al grupo del documento. Los eventos (los mismos nombres en Ariadna):
    /// «haCambiado(grupo, dispositivo)», «lectura(grupo, dispositivo, LecturaAlmacenAvisoDTO)», «lecturas(grupo, dispositivo,
    /// [LecturaAlmacenAvisoDTO])» y «correccion(grupo, dispositivo, CorreccionAlmacenAvisoDTO)».
    /// </summary>
    public class AvisosAlmacenSignalR : IAvisosAlmacen, Agencias.RepartoPropio.IAvisosReparto, Agencias.RepartoPropio.IAvisosCobroReparto
    {
        private readonly Func<IHubContext> contexto;
        private readonly Func<ModoAvisosConDatos> modo;

        public AvisosAlmacenSignalR() : this(() => GlobalHost.ConnectionManager.GetHubContext<AlmacenHub>(),
            () => AvisosAlmacen.LeerModo(ConfigurationManager.AppSettings[AvisosAlmacen.CLAVE_AVISOS_CON_DATOS]))
        {
        }

        internal AvisosAlmacenSignalR(Func<IHubContext> contexto, Func<ModoAvisosConDatos> modo = null)
        {
            this.contexto = contexto;
            this.modo = modo ?? (() => ModoAvisosConDatos.Ambos);
        }

        public void HaCambiado(string grupo, string usuario, string conexionEmisora, string dispositivoEmisor)
        {
            Mandar(grupo, usuario, conexionEmisora, dispositivoEmisor, (clientes, limpio, dispositivo) => clientes.haCambiado(limpio, dispositivo));
        }

        /// <summary>
        /// NestoAPI#759: «repartoCambiado(RepartoCambiadoDTO)» al grupo «Reparto», a todos menos a la PDA que lo ha hecho (como los
        /// demás avisos). Sin datos de las paradas: quien lo recibe relee GET api/RepartoPropio/Envios. Nunca lanza.
        /// </summary>
        public void RepartoCambiado(string usuario, string motivo, DateTime fechaHora, Agencias.RepartoPropio.OrigenPeticionReparto origen)
        {
            Mandar(AvisosAlmacen.GRUPO_REPARTO, usuario, origen?.Conexion, origen?.Dispositivo, (clientes, limpio, dispositivo) =>
                clientes.repartoCambiado(new global::NestoAPI.Models.Agencias.RepartoCambiadoDTO
                {
                    Usuario = Agencias.RepartoPropio.ServicioRepartoPropio.NombreRepartidor(usuario),
                    Motivo = motivo,
                    FechaHora = fechaHora,
                    Dispositivo = dispositivo
                }));
        }

        /// <summary>
        /// NestoAPI#773: «pagoCompletado(PagoCompletadoRepartoDTO)» al grupo «PagoTPV:{id}» (la PDA que generó el enlace). Sin exclusiones:
        /// el cambio no lo ha hecho ninguna PDA, sino Redsys. Nunca lanza.
        /// </summary>
        public void PagoCompletado(Agencias.RepartoPropio.PagoCompletadoRepartoDTO pago)
        {
            if (pago != null)
            {
                MandarATodos(AvisosAlmacen.GrupoPagoTPV(pago.PagoTPVId), clientes => clientes.pagoCompletado(pago));
            }
        }

        /// <summary>NestoAPI#773: «autorizacionRespondida(AutorizacionReembolsoDTO)» al grupo «AutorizacionReembolso:{id}». Nunca lanza.</summary>
        public void AutorizacionRespondida(Agencias.RepartoPropio.AutorizacionReembolsoDTO autorizacion)
        {
            if (autorizacion != null)
            {
                MandarATodos(AvisosAlmacen.GrupoAutorizacion(autorizacion.Id), clientes => clientes.autorizacionRespondida(autorizacion));
            }
        }

        private void MandarATodos(string grupo, Action<dynamic> enviar)
        {
            try
            {
                enviar(contexto().Clients.Group(grupo));
            }
            catch (Exception ex)
            {
                try
                {
                    ElmahHelper.Log(new Exception($"[SignalR almacén] No se pudo avisar al grupo {grupo}: " + ex.Message, ex));
                }
                catch
                {
                    // nada
                }
            }
        }

        public void Lecturas(string grupo, string usuario, string conexionEmisora, string dispositivoEmisor, IReadOnlyList<LecturaAlmacenAvisoDTO> lecturas)
        {
            List<LecturaAlmacenAvisoDTO> lista = (lecturas ?? new List<LecturaAlmacenAvisoDTO>()).Where(l => l != null).ToList();
            if (lista.Count == 0)
            {
                return;
            }
            ModoAvisosConDatos ahora = Modo();
            Mandar(grupo, usuario, conexionEmisora, dispositivoEmisor, (clientes, limpio, dispositivo) =>
            {
                if (ahora != ModoAvisosConDatos.SoloHaCambiado)
                {
                    foreach (LecturaAlmacenAvisoDTO lectura in lista)
                    {
                        lectura.SigueHaCambiado = ahora == ModoAvisosConDatos.Ambos;
                    }
                    if (lista.Count == 1)
                    {
                        clientes.lectura(limpio, dispositivo, lista[0]);
                    }
                    else
                    {
                        clientes.lecturas(limpio, dispositivo, lista);
                    }
                }
                if (ahora != ModoAvisosConDatos.SoloDatos)
                {
                    clientes.haCambiado(limpio, dispositivo);
                }
            });
        }

        public void Correccion(string grupo, string usuario, string conexionEmisora, string dispositivoEmisor, CorreccionAlmacenAvisoDTO correccion)
        {
            if (correccion == null)
            {
                return;
            }
            ModoAvisosConDatos ahora = Modo();
            Mandar(grupo, usuario, conexionEmisora, dispositivoEmisor, (clientes, limpio, dispositivo) =>
            {
                if (ahora != ModoAvisosConDatos.SoloHaCambiado)
                {
                    correccion.SigueHaCambiado = ahora == ModoAvisosConDatos.Ambos;
                    clientes.correccion(limpio, dispositivo, correccion);
                }
                if (ahora != ModoAvisosConDatos.SoloDatos)
                {
                    clientes.haCambiado(limpio, dispositivo);
                }
            });
        }

        private ModoAvisosConDatos Modo()
        {
            try
            {
                return modo();
            }
            catch
            {
                return ModoAvisosConDatos.Ambos;
            }
        }

        /// <summary>
        /// Lo común: el grupo bien escrito, a todos menos a la PDA que lo ha hecho, y si está sola no se manda nada. Los eventos
        /// del mismo cambio van seguidos al mismo grupo (SignalR los entrega en ese orden a cada conexión). Nunca lanza.
        /// </summary>
        private void Mandar(string grupo, string usuario, string conexionEmisora, string dispositivoEmisor, Action<dynamic, string, string> enviar)
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
                dynamic clientes = contexto().Clients.Group(limpio, excluidas.ToArray());
                enviar(clientes, limpio, dispositivoEmisor?.Trim() ?? string.Empty);
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
