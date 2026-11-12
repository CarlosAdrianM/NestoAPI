using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>
    /// NestoAPI#857: un correo de la bandeja de entrada del buzón de Compras, tal como lo trae el lector. El cuerpo solo vive en
    /// memoria durante la pasada: nunca se guarda entero (de los que traen seguimiento, solo un extracto corto).
    /// </summary>
    public class CorreoCompras
    {
        private List<string> enlaces;
        private string texto;

        /// <summary>Id del mensaje en Graph (cambia si se mueve de carpeta).</summary>
        public string Id { get; set; }
        /// <summary>Cabecera Message-ID (internetMessageId): la clave de la idempotencia.</summary>
        public string MensajeId { get; set; }
        public DateTime Recibido { get; set; }
        public string RemitenteDireccion { get; set; }
        public string RemitenteNombre { get; set; }
        public string Asunto { get; set; }
        public string Cuerpo { get; set; }
        public bool CuerpoEsHtml { get; set; }

        /// <summary>Dominio del remitente directo, en minúsculas (null si no hay dirección).</summary>
        public string RemitenteDominio => TextoCorreo.Dominio(RemitenteDireccion);

        /// <summary>Los enlaces del cuerpo, ya desenvueltos (safelinks, redirecciones) y sin repetir. Se calculan una vez.</summary>
        public List<string> Enlaces => enlaces ?? (enlaces = ExtractorEnlaces.Extraer(Cuerpo, CuerpoEsHtml));

        /// <summary>El cuerpo como texto plano (sin etiquetas, con saltos de línea), para buscar códigos y para el extracto.</summary>
        public string Texto => texto ?? (texto = CuerpoEsHtml ? TextoCorreo.HtmlATexto(Cuerpo) : (Cuerpo ?? string.Empty));

        /// <summary>
        /// La clave única que se guarda: el Message-ID; si no hay, el id de Graph. Si pasa de 400 caracteres (no cabría en el
        /// índice único), su SHA-256.
        /// </summary>
        public string Clave()
        {
            string id = !string.IsNullOrWhiteSpace(MensajeId) ? MensajeId.Trim() : Id?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            if (id.Length <= 400)
            {
                return id;
            }
            using (SHA256 sha = SHA256.Create())
            {
                return "sha256:" + string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(id)).Select(b => b.ToString("x2")));
            }
        }
    }

    /// <summary>NestoAPI#857: utilidades puras de texto de correo.</summary>
    public static class TextoCorreo
    {
        private static readonly Regex EtiquetasBloque = new Regex(@"<\s*(br|/p|/div|/tr|/li|/h[1-6]|/table|p|div|tr|li)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ScriptsYEstilos = new Regex(@"<\s*(script|style|head)\b[^>]*>.*?<\s*/\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex Comentarios = new Regex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex Etiquetas = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex EspaciosEnLinea = new Regex(@"[ \t ]+", RegexOptions.Compiled);
        private static readonly Regex LineasVacias = new Regex(@"\n\s*\n+", RegexOptions.Compiled);
        private static readonly Regex Direccion = new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
        /// <summary>«De: …» / «From: …» de un correo reenviado (Outlook, Gmail, Apple Mail).</summary>
        private static readonly Regex CabeceraReenvio = new Regex(@"^\s*\*?\s*(?:De|From|Remitente|Von)\s*\*?\s*:\s*(.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        public static string HtmlATexto(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return string.Empty;
            }
            string texto = Comentarios.Replace(html, " ");
            texto = ScriptsYEstilos.Replace(texto, " ");
            texto = EtiquetasBloque.Replace(texto, "\n");
            texto = Etiquetas.Replace(texto, " ");
            texto = WebUtility.HtmlDecode(texto).Replace("\r", string.Empty);
            texto = EspaciosEnLinea.Replace(texto, " ");
            texto = string.Join("\n", texto.Split('\n').Select(l => l.Trim()));
            return LineasVacias.Replace(texto, "\n").Trim();
        }

        /// <summary>El dominio de una dirección de correo, en minúsculas; null si no lo parece.</summary>
        public static string Dominio(string direccion)
        {
            if (string.IsNullOrWhiteSpace(direccion))
            {
                return null;
            }
            int arroba = direccion.LastIndexOf('@');
            string dominio = (arroba >= 0 ? direccion.Substring(arroba + 1) : direccion).Trim().Trim('>', '.', ' ').ToLowerInvariant();
            return dominio.Contains(".") && !dominio.Contains(" ") ? dominio : null;
        }

        /// <summary>
        /// Las direcciones de los remitentes ORIGINALES de un correo reenviado (líneas «De:»/«From:» del cuerpo), en minúsculas y sin
        /// repetir, en el orden en que aparecen.
        /// </summary>
        public static List<string> RemitentesOriginales(string texto)
        {
            var direcciones = new List<string>();
            if (string.IsNullOrEmpty(texto))
            {
                return direcciones;
            }
            foreach (Match linea in CabeceraReenvio.Matches(texto))
            {
                Match direccion = Direccion.Match(linea.Groups[1].Value);
                if (direccion.Success)
                {
                    string limpia = direccion.Value.ToLowerInvariant();
                    if (!direcciones.Contains(limpia))
                    {
                        direcciones.Add(limpia);
                    }
                }
            }
            return direcciones;
        }

        public static string Recortar(string texto, int maximo)
        {
            if (texto == null)
            {
                return null;
            }
            string limpio = texto.Trim();
            return limpio.Length <= maximo ? limpio : limpio.Substring(0, maximo);
        }

        /// <summary>¿El host es ese dominio o un subdominio suyo? («correos.es» no casa con «correosexpress.com»).</summary>
        public static bool HostDe(string host, string dominio)
        {
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(dominio))
            {
                return false;
            }
            string h = host.Trim().TrimEnd('.').ToLowerInvariant();
            string d = dominio.Trim().TrimStart('@', '.').ToLowerInvariant();
            return h == d || h.EndsWith("." + d, StringComparison.Ordinal);
        }

        /// <summary>Mayúsculas, sin tildes y con un solo espacio entre palabras (para buscar nombres en el texto).</summary>
        public static string Normalizar(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return string.Empty;
            }
            string descompuesto = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(descompuesto.Length);
            foreach (char c in descompuesto)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }
                sb.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
            }
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }
    }
}
