using Elmah;
using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Seguridad (25/11/26): ELMAH guardaba en cada error TODAS las variables del servidor, la cabecera <c>Authorization</c> incluida
    /// (HTTP_AUTHORIZATION y, dentro, ALL_HTTP y ALL_RAW): el JWT entero, válido durante semanas. Quien viera un error (o lo copiara en un
    /// correo o una issue) tenía la sesión del usuario. También la cookie, la X-API-KEY y, en /oauth/token, la contraseña del formulario.
    ///
    /// <para>LA REGLA (pura): una cabecera, variable, campo del formulario o parámetro es SENSIBLE si su nombre (sin mayúsculas, guiones,
    /// subrayados ni el prefijo HTTP_) contiene AUTHORIZATION, COOKIE, TOKEN, SECRET, PASSWORD o APIKEY; su valor pasa a «(oculto)»
    /// (en Authorization se deja el esquema: «Bearer (oculto)», que sí sirve para diagnosticar). En ALL_HTTP y ALL_RAW (una cabecera por
    /// línea, «NOMBRE:valor») se ocultan solo esas líneas. Las cookies, todas (solo llevan sesiones). Lo demás no se toca: el usuario ya
    /// consta en <c>user</c> y AUTH_USER. Nunca lanza.</para>
    /// </summary>
    public static class ReglaElmahSinCredenciales
    {
        public const string OCULTO = "(oculto)";
        private static readonly string[] PALABRAS = { "AUTHORIZATION", "COOKIE", "TOKEN", "SECRET", "PASSWORD", "APIKEY" };
        private static readonly string[] BLOQUES = { "ALL_HTTP", "ALL_RAW" };
        // Una línea «Nombre: valor» (ALL_RAW) o «HTTP_NOMBRE:valor» (ALL_HTTP)
        private static readonly Regex LINEA = new Regex(@"^(?<nombre>[^:\r\n]+):(?<espacio>[ \t]*)(?<valor>[^\r\n]*)(?<fin>\r?)$", RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>¿El nombre es de algo que no debe guardarse?</summary>
        public static bool EsNombreSensible(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return false;
            }
            string normalizado = new string(nombre.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            return PALABRAS.Any(p => normalizado.Contains(p));
        }

        /// <summary>El valor que se guarda en lugar del de verdad (null o vacío se quedan como están).</summary>
        public static string Ocultar(string nombre, string valor)
        {
            if (string.IsNullOrEmpty(valor))
            {
                return valor;
            }
            string normalizado = new string((nombre ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            if (normalizado.Contains("AUTHORIZATION"))
            {
                string recortado = valor.Trim();
                int espacio = recortado.IndexOf(' ');
                string esquema = espacio > 0 ? recortado.Substring(0, espacio) : null;
                if (esquema != null && esquema.All(char.IsLetter))
                {
                    return $"{esquema} {OCULTO}";
                }
            }
            return OCULTO;
        }

        /// <summary>ALL_HTTP / ALL_RAW: las líneas de cabeceras sensibles, con el valor oculto; el resto, igual.</summary>
        public static string OcultarEnBloque(string bloque)
        {
            if (string.IsNullOrEmpty(bloque))
            {
                return bloque;
            }
            return LINEA.Replace(bloque, m =>
            {
                string nombre = m.Groups["nombre"].Value;
                return EsNombreSensible(nombre)
                    ? $"{nombre}:{m.Groups["espacio"].Value}{Ocultar(nombre, m.Groups["valor"].Value)}{m.Groups["fin"].Value}"
                    : m.Value;
            });
        }

        // Un parámetro «nombre=valor» de una query string o una URL (?access_token=…)
        private static readonly Regex PARAMETRO = new Regex(@"(?<pre>^|[?&;])(?<nombre>[^=&;?#\r\n]+)=(?<valor>[^&;#\r\n]*)", RegexOptions.Compiled);
        private static readonly string[] CON_PARAMETROS = { "QUERY_STRING", "HTTP_REFERER", "UNENCODED_URL", "HTTP_URL", "CACHE_URL" };

        /// <summary>QUERY_STRING, el Referer…: los parámetros de nombre sensible, con el valor oculto.</summary>
        public static string OcultarParametros(string texto)
        {
            if (string.IsNullOrEmpty(texto) || texto.IndexOf('=') < 0)
            {
                return texto;
            }
            return PARAMETRO.Replace(texto, m =>
            {
                string nombre = m.Groups["nombre"].Value;
                string legible;
                try
                {
                    legible = Uri.UnescapeDataString(nombre);
                }
                catch
                {
                    legible = nombre;
                }
                return EsNombreSensible(legible) && m.Groups["valor"].Length > 0
                    ? $"{m.Groups["pre"].Value}{nombre}={OCULTO}"
                    : m.Value;
            });
        }

        /// <summary>Limpia una colección en el sitio (variables del servidor, formulario, query string o cookies).</summary>
        public static void Limpiar(NameValueCollection coleccion, bool todas = false)
        {
            if (coleccion == null || coleccion.Count == 0)
            {
                return;
            }
            foreach (string clave in coleccion.AllKeys.Where(k => k != null).ToList())
            {
                string valor = coleccion[clave];
                if (todas || EsNombreSensible(clave))
                {
                    coleccion[clave] = todas ? (string.IsNullOrEmpty(valor) ? valor : OCULTO) : Ocultar(clave, valor);
                }
                else if (BLOQUES.Contains(clave, StringComparer.OrdinalIgnoreCase))
                {
                    coleccion[clave] = OcultarParametros(OcultarEnBloque(valor));
                }
                else if (CON_PARAMETROS.Contains(clave, StringComparer.OrdinalIgnoreCase))
                {
                    coleccion[clave] = OcultarParametros(valor);
                }
            }
        }

        /// <summary>Una copia del error sin credenciales (el original no se toca). Si algo falla, el error tal cual: nunca se pierde un error.</summary>
        public static Error Limpiar(Error error)
        {
            if (error == null)
            {
                return null;
            }
            try
            {
                Error copia = (Error)((ICloneable)error).Clone();
                Limpiar(copia.ServerVariables);
                Limpiar(copia.Form);
                Limpiar(copia.QueryString);
                Limpiar(copia.Cookies, todas: true);
                return copia;
            }
            catch
            {
                return error;
            }
        }
    }

    /// <summary>
    /// El <c>errorLog</c> de ELMAH (Web.config, sección elmah): el SqlErrorLog de siempre, que antes de guardar quita las credenciales
    /// (<see cref="ReglaElmahSinCredenciales"/>). Punto único: pasan por aquí el módulo (errores sin capturar y ErrorSignal), ElmahHelper
    /// y las llamadas directas a ErrorLog.GetDefault(...).Log. Leer (el panel /logs-nestoapi) es lo de siempre.
    /// </summary>
    public class ErrorLogSinCredenciales : SqlErrorLog
    {
        public ErrorLogSinCredenciales(IDictionary config) : base(config)
        {
        }

        public ErrorLogSinCredenciales(string connectionString) : base(connectionString)
        {
        }

        public override string Log(Error error)
        {
            return base.Log(ReglaElmahSinCredenciales.Limpiar(error));
        }
    }
}
