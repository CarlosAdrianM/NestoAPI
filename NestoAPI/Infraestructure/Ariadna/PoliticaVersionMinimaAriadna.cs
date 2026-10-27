using System;
using System.Configuration;

namespace NestoAPI.Infraestructure.Ariadna
{
    /// <summary>Cómo actúa la API con una Ariadna por debajo de la versión mínima.</summary>
    public enum ModoVersionMinima
    {
        /// <summary>Solo apunta en ELMAH quién va desactualizado (una vez por dispositivo y día). No bloquea. Es el modo por defecto.</summary>
        Sombra,
        /// <summary>Contesta 426 Upgrade Required: Ariadna enseña el mensaje con un botón a su ficha de Play Store.</summary>
        Activo
    }

    /// <summary>
    /// NestoAPI#657: la versión mínima de Ariadna que acepta la API. El 16/10/26 había PDA con la 2.6 cuando estaba
    /// publicada la 2.11 y una reposición se terminó sin bultos ni etiquetas. Las reglas puras (sin HTTP ni
    /// configuración) para poder probarlas; <see cref="VersionMinimaAriadnaHandler"/> las aplica a cada petición.
    ///   - Ariadna manda su versión en la cabecera <see cref="CABECERA_VERSION"/> (hoy solo en las sugerencias; en
    ///     todas las llamadas desde la issue gemela de Ariadna).
    ///   - Sin cabecera (Ariadna anteriores, Nesto, NestoApp, la tienda): no se mira nada. Solo Ariadna manda la
    ///     cabecera, así que Nesto y NestoApp no se ven afectados aunque compartan endpoints con el almacén.
    ///   - Cabecera que no se entiende: se deja pasar (fail-open), nunca se bloquea por un dato raro.
    /// </summary>
    public class PoliticaVersionMinimaAriadna
    {
        public const string CABECERA_VERSION = "X-Ariadna-Version";
        public const string CLAVE_VERSION_MINIMA = "Ariadna:VersionMinima";
        public const string CLAVE_MODO = "Ariadna:VersionMinimaModo";
        public const string CODIGO_ERROR = "ARIADNA_DESACTUALIZADA";

        /// <param name="versionMinima">null = sin mínima: no se bloquea ni se apunta nada.</param>
        public PoliticaVersionMinimaAriadna(Version versionMinima, ModoVersionMinima modo)
        {
            VersionMinima = versionMinima;
            Modo = modo;
        }

        public Version VersionMinima { get; }
        public ModoVersionMinima Modo { get; }

        public bool Bloquea => Modo == ModoVersionMinima.Activo;

        /// <summary>Del Web.config: <c>Ariadna:VersionMinima</c> (p. ej. 2.18) y <c>Ariadna:VersionMinimaModo</c> (Sombra / Activo).</summary>
        public static PoliticaVersionMinimaAriadna DesdeConfiguracion() =>
            Crear(ConfigurationManager.AppSettings[CLAVE_VERSION_MINIMA], ConfigurationManager.AppSettings[CLAVE_MODO]);

        /// <summary>Con los dos valores tal cual vienen del Web.config. Mínima que no se entiende = sin mínima; modo que no se entiende = Sombra.</summary>
        public static PoliticaVersionMinimaAriadna Crear(string versionMinima, string modo) =>
            new PoliticaVersionMinimaAriadna(Parsear(versionMinima), LeerModo(modo));

        /// <summary>Vacío o desconocido: <see cref="ModoVersionMinima.Sombra"/>, que es el que no hace daño.</summary>
        public static ModoVersionMinima LeerModo(string valor)
        {
            return Enum.TryParse((valor ?? string.Empty).Trim(), true, out ModoVersionMinima modo) && Enum.IsDefined(typeof(ModoVersionMinima), modo)
                ? modo
                : ModoVersionMinima.Sombra;
        }

        /// <summary>«2.6», «2.18» o «2.18.1»; también «3» (una sola cifra). null si no se entiende.</summary>
        public static Version Parsear(string version)
        {
            string limpia = (version ?? string.Empty).Trim();
            if (limpia.Length == 0)
            {
                return null;
            }
            if (Version.TryParse(limpia, out Version v))
            {
                return v;
            }
            return int.TryParse(limpia, out int mayor) && mayor >= 0 ? new Version(mayor, 0) : null;
        }

        /// <summary>
        /// true si la cabecera trae una versión que se entiende y es menor que la mínima. Sin cabecera, sin mínima o
        /// con una versión rara: false (no se toca a nadie por eso).
        /// </summary>
        public bool EstaDesactualizada(string cabeceraVersion, out Version version)
        {
            version = Parsear(cabeceraVersion);
            return VersionMinima != null && version != null && version < VersionMinima;
        }

        /// <summary>De usted (Carlos 15/10): «Su Ariadna (2.6) está desactualizada: actualícela desde Play Store para seguir.»</summary>
        public static string Mensaje(Version version) =>
            $"Su Ariadna ({version}) está desactualizada: actualícela desde Play Store para seguir.";

        /// <summary>Lo que va a ELMAH en modo sombra (y en activo, para saber a quién se ha bloqueado).</summary>
        public string MensajeElmah(Version version, string dispositivo, string usuario) =>
            $"[VersionMinimaAriadna] Ariadna {version} por debajo de la mínima {VersionMinima} ({Modo}). " +
            $"Dispositivo: {(string.IsNullOrWhiteSpace(dispositivo) ? "desconocido" : dispositivo)}; usuario: {(string.IsNullOrWhiteSpace(usuario) ? "desconocido" : usuario)}.";
    }
}
