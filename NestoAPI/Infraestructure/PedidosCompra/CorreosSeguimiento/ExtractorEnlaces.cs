using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>
    /// NestoAPI#857: saca los enlaces de un cuerpo de correo (HTML o texto) y los desenvuelve SIN hacer peticiones: safelinks de
    /// Outlook, urldefense (Proofpoint v2), google.com/url y cualquier redirección de seguimiento de clics que lleve el destino
    /// en claro en un parámetro (url, u, redirect, target, link, dest…). Lo que no se puede desenvolver sin seguir el enlace
    /// (p. ej. un id opaco de Mailchimp) se queda tal cual.
    /// </summary>
    public static class ExtractorEnlaces
    {
        public const int MAXIMO_ENLACES = 200;
        private const int MAXIMO_VUELTAS = 5;

        private static readonly Regex Href = new Regex(@"href\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex UrlEnTexto = new Regex(@"https?://[^\s""'<>\]\[]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly char[] PuntuacionFinal = { '.', ',', ';', ':', '!', '?', ')', '>', '»', '"', '\'' };

        /// <summary>Parámetros que suelen llevar el destino de una redirección.</summary>
        internal static readonly string[] ParametrosDestino =
        {
            "url", "u", "redirect", "redirect_url", "redirecturl", "redirect_uri", "target", "targeturl", "link", "dest",
            "destination", "goto", "r"
        };

        public static List<string> Extraer(string cuerpo, bool esHtml)
        {
            var resultado = new List<string>();
            if (string.IsNullOrWhiteSpace(cuerpo))
            {
                return resultado;
            }
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> crudos = esHtml
                ? Href.Matches(cuerpo).Cast<Match>().Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value)
                    .Concat(UrlEnTexto.Matches(WebUtility.HtmlDecode(TextoCorreo.HtmlATexto(cuerpo))).Cast<Match>().Select(m => m.Value))
                : UrlEnTexto.Matches(cuerpo).Cast<Match>().Select(m => m.Value);
            foreach (string crudo in crudos)
            {
                string enlace = Limpiar(WebUtility.HtmlDecode(crudo ?? string.Empty));
                if (enlace == null)
                {
                    continue;
                }
                enlace = Desenvolver(enlace);
                if (vistos.Add(enlace))
                {
                    resultado.Add(enlace);
                    if (resultado.Count >= MAXIMO_ENLACES)
                    {
                        break;
                    }
                }
            }
            return resultado;
        }

        private static string Limpiar(string enlace)
        {
            string limpio = enlace.Trim().TrimEnd(PuntuacionFinal);
            return Uri.TryCreate(limpio, UriKind.Absolute, out Uri uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? limpio
                : null;
        }

        /// <summary>Quita capas de redirección (hasta <see cref="MAXIMO_VUELTAS"/>) mientras el destino esté en claro.</summary>
        public static string Desenvolver(string enlace)
        {
            string actual = enlace;
            for (int vuelta = 0; vuelta < MAXIMO_VUELTAS; vuelta++)
            {
                string siguiente = UnaCapa(actual);
                if (siguiente == null || string.Equals(siguiente, actual, StringComparison.Ordinal))
                {
                    break;
                }
                actual = siguiente;
            }
            return actual;
        }

        private static string UnaCapa(string enlace)
        {
            if (!Uri.TryCreate(enlace, UriKind.Absolute, out Uri uri) || string.IsNullOrEmpty(uri.Query))
            {
                return null;
            }
            NameValueCollection parametros;
            try
            {
                parametros = HttpUtility.ParseQueryString(uri.Query);
            }
            catch (Exception)
            {
                return null;
            }
            string host = uri.Host.ToLowerInvariant();

            if (TextoCorreo.HostDe(host, "urldefense.proofpoint.com") && uri.AbsolutePath.StartsWith("/v2/", StringComparison.OrdinalIgnoreCase))
            {
                // Proofpoint v2: u=https-3A__www.ejemplo.com_ruta (-XX = %XX, _ = /)
                string u = parametros["u"];
                if (!string.IsNullOrEmpty(u))
                {
                    string destino = Uri.UnescapeDataString(u.Replace('_', '/').Replace('-', '%'));
                    return EsHttp(destino) ? destino : null;
                }
            }

            if ((host.StartsWith("google.", StringComparison.Ordinal) || host.StartsWith("www.google.", StringComparison.Ordinal))
                && uri.AbsolutePath.Equals("/url", StringComparison.OrdinalIgnoreCase))
            {
                string q = parametros["q"];
                if (!string.IsNullOrEmpty(q) && EsHttp(q.Trim()))
                {
                    return Limpiar(q.Trim());
                }
            }

            // safelinks de Outlook (url=) y las redirecciones de clics con el destino en claro
            foreach (string clave in ParametrosDestino)
            {
                string valor = parametros[clave];
                if (!string.IsNullOrEmpty(valor) && EsHttp(valor.Trim()))
                {
                    return Limpiar(valor.Trim());
                }
            }
            return null;
        }

        private static bool EsHttp(string valor) =>
            valor.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || valor.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        /// <summary>Host de un enlace en minúsculas; null si no es una URL.</summary>
        public static string Host(string enlace) =>
            Uri.TryCreate(enlace, UriKind.Absolute, out Uri uri) ? uri.Host.ToLowerInvariant() : null;
    }
}
