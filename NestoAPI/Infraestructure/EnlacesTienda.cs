using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// NestoAPI#706: el único sitio donde se construyen los enlaces a la tienda online (y a Google Play) que van en los
    /// correos. Todo enlace a la tienda en un correo a clientes lleva utm_source, utm_medium y utm_campaign para poder
    /// medir si produce ventas. Quien da la URL (ProductoDTO.RutaEnlace, ProductoDTO.LeerUrlTiendaOnline) la da LIMPIA,
    /// y cada correo le pone aquí las suyas.
    /// </summary>
    public static class EnlacesTienda
    {
        public const string URL_TIENDA = "https://www.productosdeesteticaypeluqueriaprofesional.com/";
        /// <summary>La landing de ofertas a la que llevaba la firma de los correos de envío (antes por bit.ly).</summary>
        public const string URL_OFERTAS = "https://www.productosdeesteticaypeluqueriaprofesional.com/40110-ofertas";
        public const string URL_GOOGLE_PLAY = "https://play.google.com/store/apps/details?id=com.nuevavision.nestotiendas";

        public const string MEDIUM_EMAIL = "email";

        /// <summary>
        /// Los enlaces que Nesto (escritorio) y la API enseñan en pantalla, que no son correos: se mantienen como antes
        /// (utm_source=nuevavision&amp;utm_campaign=nesto, sin medium).
        /// </summary>
        public const string SOURCE_NESTO_PANTALLA = "nuevavision";
        public const string CAMPAIGN_NESTO_PANTALLA = "nesto";

        /// <summary>
        /// La URL con sus UTM: quita las utm_* que traiga (de quien sea), conserva el resto de parámetros y el
        /// fragmento (#…), y añade las que se le pasan en este orden: source, medium, campaign, content y term. Las
        /// que vengan vacías no se ponen. Null si la URL es nula, vacía o no es una URL http(s) absoluta.
        /// </summary>
        public static string ConUtm(string url, string source, string medium, string campaign,
            string content = null, string term = null)
        {
            string limpia = UrlValida(url);
            if (limpia == null)
            {
                return null;
            }

            SepararFragmento(QuitarUtm(limpia), out string sinFragmento, out string fragmento);

            var utm = new List<string>();
            Anadir(utm, "utm_source", source);
            Anadir(utm, "utm_medium", medium);
            Anadir(utm, "utm_campaign", campaign);
            Anadir(utm, "utm_content", content);
            Anadir(utm, "utm_term", term);
            if (!utm.Any())
            {
                return sinFragmento + fragmento;
            }

            string separador = sinFragmento.Contains("?") ? "&" : "?";
            return sinFragmento + separador + string.Join("&", utm) + fragmento;
        }

        /// <summary>Para los enlaces que se ven en Nesto/la API (no correos): los de siempre.</summary>
        public static string ParaPantallaNesto(string url)
            => ConUtm(url, SOURCE_NESTO_PANTALLA, null, CAMPAIGN_NESTO_PANTALLA);

        /// <summary>
        /// Quita los parámetros utm_* de una URL conservando el resto y el fragmento. Nula o vacía se devuelve tal cual.
        /// </summary>
        public static string QuitarUtm(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }

            SepararFragmento(url, out string sinFragmento, out string fragmento);
            int indiceQuery = sinFragmento.IndexOf('?');
            if (indiceQuery < 0)
            {
                return url;
            }

            string urlBase = sinFragmento.Substring(0, indiceQuery);
            List<string> parametros = sinFragmento.Substring(indiceQuery + 1)
                .Split('&')
                .Where(p => p.Length > 0 && !p.StartsWith("utm_", StringComparison.OrdinalIgnoreCase))
                .ToList();

            return (parametros.Any() ? urlBase + "?" + string.Join("&", parametros) : urlBase) + fragmento;
        }

        /// <summary>
        /// El enlace a la app en Google Play con el referrer de instalación (utm_source=&lt;correo&gt;, utm_medium=email),
        /// que es lo que Google Play Console atribuye.
        /// </summary>
        public static string GooglePlay(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return URL_GOOGLE_PLAY;
            }
            string referrer = "utm_source=" + source.Trim() + "&utm_medium=" + MEDIUM_EMAIL;
            return URL_GOOGLE_PLAY + "&referrer=" + Uri.EscapeDataString(referrer);
        }

        /// <summary>La URL recortada si es http(s) absoluta con host; null si no.</summary>
        internal static string UrlValida(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }
            string recortada = url.Trim();
            if (!Uri.TryCreate(recortada, UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || string.IsNullOrWhiteSpace(uri.Host))
            {
                return null;
            }
            return recortada;
        }

        private static void Anadir(List<string> utm, string nombre, string valor)
        {
            if (!string.IsNullOrWhiteSpace(valor))
            {
                utm.Add(nombre + "=" + Uri.EscapeDataString(valor.Trim()));
            }
        }

        private static void SepararFragmento(string url, out string sinFragmento, out string fragmento)
        {
            int indice = url.IndexOf('#');
            sinFragmento = indice < 0 ? url : url.Substring(0, indice);
            fragmento = indice < 0 ? string.Empty : url.Substring(indice);
        }
    }
}
