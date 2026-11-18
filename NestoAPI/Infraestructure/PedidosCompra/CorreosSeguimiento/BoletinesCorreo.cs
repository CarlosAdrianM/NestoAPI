using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>
    /// NestoAPI#857e (falso positivo del 18/11/26: boletín «Perfect Beauty News: Quies Warm» de comunicaciones@perfectbeauty.es, captado por
    /// el genérico por https://track.perfectbeauty.es/click.php?L=…&amp;J=…): un enlace de RASTREO DE CLICS de un envío masivo no es el
    /// seguimiento de un envío aunque lleve «track» en el host o la ruta. Es redirector si (1) el host es de una plataforma de envío masivo
    /// (<see cref="DominiosMailing"/>), (2) algún tramo de la ruta es de clic (<see cref="TramosClic"/>: click.php, click, clicks, ls/click…) o
    /// (3) lleva a la vez los parámetros L y J (los de phpList y similares). El genérico no cuenta estos enlaces; las demás señales (un código
    /// tras «nº de seguimiento», «tracking number»… en el texto, o una agencia registrada) siguen valiendo. PURA.
    /// </summary>
    public static class RedirectorMailing
    {
        internal static readonly string[] DominiosMailing =
        {
            "sendgrid.net", "sendgrid.com", "list-manage.com", "mailchimp.com", "mailchi.mp", "mcusercontent.com", "mailjet.com", "mjt.lu",
            "acumbamail.com", "sendinblue.com", "brevo.com", "sibautomation.com", "sendibt2.com", "sendibt3.com", "hubspot.com", "hubspotlinks.com",
            "hubspotemail.net", "hs-sites.com", "mailerlite.com", "mlsend.com", "mailrelay.com", "ip-zone.com", "klaviyo.com", "klclick.com",
            "klclick1.com", "constantcontact.com", "rs6.net", "createsend.com", "createsend1.com", "cmail19.com", "cmail20.com", "exacttarget.com",
            "exct.net", "sparkpostmail.com", "mandrillapp.com", "elasticemail.com", "getresponse.com", "acemlna.com", "acemlnb.com", "acemlnc.com",
            "acemlnd.com", "mailpoet.com", "benchmarkemail.com", "emailsys.net", "emlnk.com", "ctrk.klclick.com"
        };

        /// <summary>Tramos de ruta (en minúsculas) de los redirectores de clic.</summary>
        internal static readonly string[] TramosClic = { "click.php", "click", "clicks", "clic", "clk", "ls/click", "wf/click", "e/click", "track/click", "redirect.php" };

        public static bool Es(Uri uri)
        {
            if (uri == null)
            {
                return false;
            }
            string host = uri.Host.ToLowerInvariant();
            if (DominiosMailing.Any(d => TextoCorreo.HostDe(host, d)))
            {
                return true;
            }
            string ruta = uri.AbsolutePath.ToLowerInvariant().Trim('/');
            string[] tramos = ruta.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string clic in TramosClic)
            {
                if (clic.Contains("/")
                    ? ("/" + ruta + "/").Contains("/" + clic + "/")
                    : tramos.Contains(clic))
                {
                    return true;
                }
            }
            NameValueCollection parametros = string.IsNullOrEmpty(uri.Query) ? new NameValueCollection() : HttpUtility.ParseQueryString(uri.Query);
            var claves = new HashSet<string>(parametros.AllKeys.Where(k => k != null), StringComparer.OrdinalIgnoreCase);
            return claves.Contains("L") && claves.Contains("J");
        }

        public static bool Es(string enlace) => Uri.TryCreate(enlace, UriKind.Absolute, out Uri uri) && Es(uri);
    }

    /// <summary>NestoAPI#857e: qué remitente se ignora al descartar un correo: su dirección exacta, en minúsculas; nunca una nuestra. PURA.</summary>
    public static class ReglaIgnorarRemitente
    {
        public static string Remitente(string direccion)
        {
            string limpia = direccion?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(limpia) || limpia.Length > 255 || !limpia.Contains("@") || TextoCorreo.Dominio(limpia) == null
                || AsignadorPedidosCompra.EsPropio(TextoCorreo.Dominio(limpia)))
            {
                return null;
            }
            return limpia;
        }
    }

    /// <summary>
    /// NestoAPI#857e: ¿es un boletín comercial? Por el texto («darse de baja», «unsubscribe», «ver en el navegador»…; sin mayúsculas ni
    /// tildes) o por el remitente (comunicaciones@, newsletter@, news@, marketing@, noreply-marketing@…). Graph no da List-Unsubscribe con lo
    /// que se pide (no se piden las cabeceras). Un boletín NO se guarda por la vía genérica: solo si una agencia registrada saca su seguimiento
    /// o viene de un remitente marcado. PURA.
    /// </summary>
    public static class BoletinComercial
    {
        internal static readonly string[] Frases =
        {
            "DARSE DE BAJA", "DARTE DE BAJA", "DARSE BAJA", "UNSUBSCRIBE", "VER EN EL NAVEGADOR", "VER EN TU NAVEGADOR", "VER EN SU NAVEGADOR",
            "VERLO EN TU NAVEGADOR", "VERLO EN SU NAVEGADOR", "VIEW IN BROWSER", "VIEW IN YOUR BROWSER", "VIEW IT IN YOUR BROWSER",
            "VIEW THIS EMAIL IN YOUR BROWSER", "CANCELAR LA SUSCRIPCION", "CANCELAR SUSCRIPCION", "CANCELAR TU SUSCRIPCION", "DEJAR DE RECIBIR",
            "ANULAR LA SUSCRIPCION", "BAJA DEL BOLETIN"
        };

        internal static readonly string[] Buzones =
        {
            "comunicaciones", "comunicacion", "newsletter", "newsletters", "news", "noticias", "boletin", "boletines", "marketing",
            "noreply-marketing", "no-reply-marketing", "noreply.marketing", "promociones", "promo", "mailing"
        };

        public static bool Es(CorreoCompras correo)
        {
            if (correo == null)
            {
                return false;
            }
            string direccion = correo.RemitenteDireccion?.Trim().ToLowerInvariant() ?? string.Empty;
            int arroba = direccion.IndexOf('@');
            if (arroba > 0 && Buzones.Contains(direccion.Substring(0, arroba)))
            {
                return true;
            }
            string texto = " " + TextoCorreo.Normalizar((correo.Asunto ?? string.Empty) + "\n" + correo.Texto) + " ";
            return Frases.Any(f => texto.Contains(" " + f + " "));
        }
    }
}
