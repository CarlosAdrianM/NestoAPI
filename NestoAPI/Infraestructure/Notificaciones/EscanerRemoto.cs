using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using NestoAPI.Models.EscanerRemoto;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace NestoAPI.Infraestructure.Notificaciones
{
    /// <summary>
    /// NestoAPI#768 (Carlos 30/10/26): «escáner remoto». Quien trabaja en Nesto (escritorio remoto) sin lector de códigos usa
    /// Ariadna en su móvil para leer y el código aparece en Nesto. Se casan por el MISMO USUARIO: el de Ariadna es de Identity
    /// («Alfredo») y el de Nesto, el de Windows («NUEVAVISION\Alfredo»); se comparan sin el dominio y sin distinguir mayúsculas
    /// (como <see cref="PrincipalExtensions.IsInRoleSinDominio(System.Security.Principal.IPrincipal, string)"/> con los grupos).
    /// </summary>
    public static class UsuarioEscanerRemoto
    {
        /// <summary>«NUEVAVISION\Alfredo» → «Alfredo»; «Alfredo» → «Alfredo». Null si no hay usuario.</summary>
        public static string SinDominio(string usuario)
        {
            string limpio = usuario?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            int barra = limpio.LastIndexOf('\\');
            string nombre = barra >= 0 ? limpio.Substring(barra + 1).Trim() : limpio;
            return nombre.Length == 0 ? null : nombre;
        }

        /// <summary>La clave con la que se casan: sin dominio y en mayúsculas. Null si no hay usuario.</summary>
        public static string Clave(string usuario) => SinDominio(usuario)?.ToUpperInvariant();
    }

    /// <summary>NestoAPI#768: quién puede usar el escáner remoto: cualquier usuario identificado que no sea un cliente.</summary>
    public static class EscanerRemotoPermisos
    {
        /// <summary>El token de TiendasNuevaVision (api/auth/token) lleva el claim «cliente»: un cliente no manda ni recibe lecturas.</summary>
        public static bool EsCliente(System.Security.Principal.IPrincipal usuario)
            => usuario is System.Security.Claims.ClaimsPrincipal claims && claims.Claims.Any(c => c.Type == "cliente");
    }

    /// <summary>
    /// NestoAPI#768: las conexiones de Nesto (AvisosHub) que saben recibir lecturas del escáner remoto: las que han llamado a
    /// <see cref="AvisosHub.ActividadEscanerRemoto"/> (un Nesto anterior no lo hace y no cuenta como abierto). De cada una, su
    /// usuario, su equipo y cuándo tuvo el foco por última vez: si el usuario tiene varios Nesto abiertos, el código va al que
    /// lo tuvo más recientemente. En memoria: se vacía al reciclar la API y Nesto se vuelve a presentar al reconectar.
    /// </summary>
    public class ConexionesEscanerRemoto
    {
        public static ConexionesEscanerRemoto Global { get; } = new ConexionesEscanerRemoto();

        private sealed class Conexion
        {
            public string Clave { get; set; }
            public string Equipo { get; set; }
            /// <summary>La última vez que tuvo el foco (UTC); MinValue si aún no lo ha tenido.</summary>
            public DateTime UltimoFoco { get; set; }
            /// <summary>Para desempatar entre las que no han tenido el foco: la presentada más tarde gana.</summary>
            public long Orden { get; set; }
        }

        private readonly ConcurrentDictionary<string, Conexion> conexiones = new ConcurrentDictionary<string, Conexion>();
        private long contador;

        /// <summary>Nesto se presenta (al conectar) o dice que su ventana se ha activado (<paramref name="conFoco"/>).</summary>
        public void Actividad(string conexion, string usuario, string equipo, bool conFoco, DateTime ahoraUtc)
        {
            string clave = UsuarioEscanerRemoto.Clave(usuario);
            if (string.IsNullOrWhiteSpace(conexion) || clave == null)
            {
                return;
            }
            Conexion datos = conexiones.GetOrAdd(conexion.Trim(), _ => new Conexion { UltimoFoco = DateTime.MinValue });
            lock (datos)
            {
                datos.Clave = clave;
                if (!string.IsNullOrWhiteSpace(equipo))
                {
                    datos.Equipo = equipo.Trim();
                }
                if (conFoco && ahoraUtc > datos.UltimoFoco)
                {
                    datos.UltimoFoco = ahoraUtc;
                }
                datos.Orden = System.Threading.Interlocked.Increment(ref contador);
            }
        }

        public void Quitar(string conexion)
        {
            if (!string.IsNullOrWhiteSpace(conexion))
            {
                _ = conexiones.TryRemove(conexion.Trim(), out _);
            }
        }

        /// <summary>
        /// El Nesto al que mandar lo leído por <paramref name="usuario"/>: el suyo con el foco más reciente. Conexion null si no
        /// tiene ninguno abierto. Nunca devuelve una conexión de otro usuario.
        /// </summary>
        public (string Conexion, string Equipo, int Abiertos) Elegir(string usuario)
        {
            string clave = UsuarioEscanerRemoto.Clave(usuario);
            if (clave == null)
            {
                return (null, null, 0);
            }
            var suyas = conexiones
                .Where(c => c.Value.Clave == clave)
                .Select(c => new { Id = c.Key, c.Value.Equipo, c.Value.UltimoFoco, c.Value.Orden })
                .ToList();
            if (suyas.Count == 0)
            {
                return (null, null, 0);
            }
            var elegida = suyas.OrderByDescending(c => c.UltimoFoco).ThenByDescending(c => c.Orden).First();
            return (elegida.Id, elegida.Equipo, suyas.Count);
        }

        internal void Vaciar() => conexiones.Clear();
    }

    public interface IEscanerRemoto
    {
        /// <summary>A qué Nesto iría lo que lea <paramref name="usuario"/> (sin mandar nada).</summary>
        DestinoEscanerRemotoDTO Destino(string usuario);

        /// <summary>
        /// Manda la lectura al Nesto de <paramref name="usuario"/> con el foco más reciente. Idempotente por usuario y
        /// <see cref="LecturaEscanerRemotoDTO.ClaveIdempotencia"/>. El código ya viene validado.
        /// </summary>
        DestinoEscanerRemotoDTO Enviar(string usuario, LecturaEscanerRemotoDTO lectura);
    }

    /// <summary>NestoAPI#768: el escáner remoto, con el envío por SignalR (AvisosHub) a UNA sola conexión de Nesto.</summary>
    public class ServicioEscanerRemoto : IEscanerRemoto
    {
        public const string MENSAJE_SIN_NESTO = "No hay ningún Nesto abierto con su usuario.";
        public const string MENSAJE_SIN_NESTO_AL_ENVIAR = "No hay ningún Nesto abierto con su usuario: no se ha enviado.";
        public const string MENSAJE_FALLO_ENVIO = "No se ha podido mandar a Nesto. Inténtelo otra vez.";

        /// <summary>Lo que dura la memoria de las claves de idempotencia.</summary>
        public static readonly TimeSpan DURACION_IDEMPOTENCIA = TimeSpan.FromMinutes(10);

        private readonly ConexionesEscanerRemoto conexiones;
        private readonly Action<string, LecturaEscanerRemotoAviso> mandar;
        private readonly Func<DateTime> ahoraUtc;
        private readonly ConcurrentDictionary<string, (DateTime Cuando, DestinoEscanerRemotoDTO Respuesta)> yaEnviadas =
            new ConcurrentDictionary<string, (DateTime, DestinoEscanerRemotoDTO)>();

        public ServicioEscanerRemoto()
            : this(ConexionesEscanerRemoto.Global, MandarPorSignalR, () => DateTime.UtcNow)
        { }

        internal ServicioEscanerRemoto(ConexionesEscanerRemoto conexiones, Action<string, LecturaEscanerRemotoAviso> mandar, Func<DateTime> ahoraUtc)
        {
            this.conexiones = conexiones ?? throw new ArgumentNullException(nameof(conexiones));
            this.mandar = mandar ?? throw new ArgumentNullException(nameof(mandar));
            this.ahoraUtc = ahoraUtc ?? (() => DateTime.UtcNow);
        }

        /// <summary>El evento «lecturaEscanerRemoto» del hub de Nesto, solo a esa conexión.</summary>
        public const string EVENTO_LECTURA = "lecturaEscanerRemoto";

        private static void MandarPorSignalR(string conexion, LecturaEscanerRemotoAviso aviso)
        {
            IHubContext contexto = GlobalHost.ConnectionManager.GetHubContext<AvisosHub>();
            IClientProxy cliente = contexto.Clients.Client(conexion);
            _ = cliente.Invoke(EVENTO_LECTURA, aviso);
        }

        public DestinoEscanerRemotoDTO Destino(string usuario)
        {
            string nombre = UsuarioEscanerRemoto.SinDominio(usuario);
            var (conexion, equipo, abiertos) = conexiones.Elegir(usuario);
            return conexion == null
                ? new DestinoEscanerRemotoDTO { Usuario = nombre, Mensaje = MENSAJE_SIN_NESTO }
                : new DestinoEscanerRemotoDTO
                {
                    HayNesto = true,
                    Usuario = nombre,
                    Equipo = equipo,
                    NestosAbiertos = abiertos,
                    Mensaje = "Enviando a " + NombreNesto(nombre, equipo) + "."
                };
        }

        public DestinoEscanerRemotoDTO Enviar(string usuario, LecturaEscanerRemotoDTO lectura)
        {
            if (lectura == null)
            {
                throw new ArgumentNullException(nameof(lectura));
            }
            string nombre = UsuarioEscanerRemoto.SinDominio(usuario);
            DateTime ahora = ahoraUtc();
            OlvidarAntiguas(ahora);

            string claveIdempotencia = string.IsNullOrWhiteSpace(lectura.ClaveIdempotencia)
                ? null
                : UsuarioEscanerRemoto.Clave(usuario) + "|" + lectura.ClaveIdempotencia.Trim();
            if (claveIdempotencia != null && yaEnviadas.TryGetValue(claveIdempotencia, out var anterior))
            {
                return anterior.Respuesta;
            }

            var (conexion, equipo, abiertos) = conexiones.Elegir(usuario);
            if (conexion == null)
            {
                // No se recuerda: si abre Nesto y repite con la misma clave, que vaya.
                return new DestinoEscanerRemotoDTO { Usuario = nombre, Mensaje = MENSAJE_SIN_NESTO_AL_ENVIAR };
            }

            var aviso = new LecturaEscanerRemotoAviso
            {
                Codigo = lectura.Codigo?.Trim(),
                ClaveIdempotencia = lectura.ClaveIdempotencia?.Trim(),
                FechaHora = lectura.FechaHora,
                Usuario = nombre
            };
            try
            {
                mandar(conexion, aviso);
            }
            catch (Exception ex)
            {
                try
                {
                    ElmahHelper.Log(new Exception("[EscanerRemoto] No se pudo mandar la lectura a Nesto: " + ex.Message, ex));
                }
                catch
                {
                    // nada
                }
                return new DestinoEscanerRemotoDTO
                {
                    HayNesto = true,
                    Usuario = nombre,
                    Equipo = equipo,
                    NestosAbiertos = abiertos,
                    Mensaje = MENSAJE_FALLO_ENVIO
                };
            }

            var respuesta = new DestinoEscanerRemotoDTO
            {
                HayNesto = true,
                Entregado = true,
                Usuario = nombre,
                Equipo = equipo,
                NestosAbiertos = abiertos,
                Mensaje = "Enviado a " + NombreNesto(nombre, equipo) + "."
            };
            if (claveIdempotencia != null)
            {
                yaEnviadas[claveIdempotencia] = (ahora, respuesta);
            }
            return respuesta;
        }

        /// <summary>«Nesto de Alfredo (RDS2016)»; sin equipo, «Nesto de Alfredo».</summary>
        internal static string NombreNesto(string usuario, string equipo)
            => "Nesto de " + usuario + (string.IsNullOrWhiteSpace(equipo) ? string.Empty : " (" + equipo.Trim() + ")");

        private void OlvidarAntiguas(DateTime ahora)
        {
            foreach (var entrada in yaEnviadas.Where(e => ahora - e.Value.Cuando > DURACION_IDEMPOTENCIA).ToList())
            {
                _ = yaEnviadas.TryRemove(entrada.Key, out _);
            }
        }
    }
}
