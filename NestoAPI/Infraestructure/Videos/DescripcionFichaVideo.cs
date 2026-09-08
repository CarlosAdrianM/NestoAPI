using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Videos
{
    /// <summary>
    /// Compone el texto que la tienda online publica como descripción de la ficha de un vídeo
    /// (/video/{slug}), que es además lo que Google indexa en el marcado de vídeo.
    ///
    /// <para>El motivo (equipo de SEO, 08/09/26): 34 de los 778 vídeos tienen la Descripcion vacía
    /// en Nesto, y la ficha se quedaba en la frase del título. Pero 16 de esos 34 SÍ tienen
    /// protocolo redactado por NVIA: el listado de la API simplemente no lo exponía. Antes de
    /// pedirle a nadie que escriba 23 descripciones a mano, se aprovecha lo que ya está escrito.</para>
    ///
    /// <para>Del protocolo se saca solo un ARRANQUE del tamaño de una meta-descripción, no el
    /// protocolo: el protocolo es el valor que se le da al cliente y no se regala en una página
    /// pública. Si algún día se quiere apagar esto, basta con dejar de rellenar
    /// <see cref="VideoLookupModel.DescripcionParaFicha"/> en ServicioVideos.</para>
    /// </summary>
    public static class DescripcionFichaVideo
    {
        /// <summary>
        /// Tamaño del extracto que se saca del protocolo. Google enseña ~160 caracteres; se da algo
        /// más para que la frase no muera a media idea, pero se queda muy lejos del protocolo entero.
        /// </summary>
        internal const int LongitudMaximaExtracto = 400;

        /// <summary>
        /// Un trozo de texto se considera prosa (y no un encabezado) a partir de esta longitud. Es
        /// lo que salta los "&lt;h1&gt;Protocolo Profesional de Tratamiento Estético&lt;/h1&gt;" y
        /// "&lt;h2&gt;Introducción&lt;/h2&gt;" con los que NVIA abre TODOS los protocolos: si se
        /// colasen, los 778 vídeos tendrían la misma primera frase, que para Google es peor que
        /// no tener ninguna.
        /// </summary>
        private const int LongitudMinimaProsa = 80;

        /// <summary>
        /// Lo que NVIA graba en Protocolo cuando OpenAI le devuelve algo que no sabe deserializar
        /// (GestorTranscripciones.ProcesarConOpenAI). Está en producción: el vídeo 1830
        /// ([Est. desde 0] Cap. 8: Foliculitis) tiene esto y nada más. Publicarlo como descripción
        /// sería peor que no publicar nada.
        /// </summary>
        private static readonly string[] Sentinelas =
        {
            "Error en la creación del protocolo",
            "[SIN CAPTIONS DISPONIBLES]"
        };

        /// <summary>Los enlaces del protocolo ("Ver paso en video") no aportan nada a la ficha.</summary>
        private static readonly Regex Anclas = new Regex("<a\\b[^>]*>.*?</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Encabezado que es claramente un paso del procedimiento: "Paso 1", "Paso 2:", "1.", "3)".
        /// Lo que cuelga de uno de estos NO se publica (equipo de SEO, opción C del 08/09/26).
        ///
        /// <para>La regla la propusieron ellos como garantía de no publicar procedimiento, pero lo
        /// que de verdad la justificó fue otra cosa. De los 12 extractos que se les enseñaron,
        /// ninguno revelaba dosis, tiempos ni modo de empleo —o sea que el miedo original no se
        /// materializaba— pero DOS estaban defectuosos, y esta regla se lleva los dos por delante:
        /// el 1330 tiene la frase rota de origen y el 1468 dice "un producto de la marca Eva bisn",
        /// que es Eva Visnú mal transcrita. Publicar el nombre de una marca del catálogo mal
        /// escrito, justo en el texto que Google enseña bajo el título, es peor que no publicar
        /// nada.</para>
        ///
        /// <para>Es una regla objetiva a propósito: no depende de que nadie juzgue cada texto. El
        /// precio es que también caen extractos correctos (1847, 1667), que pasan a la lista de los
        /// que se escriben a mano.</para>
        /// </summary>
        private static readonly Regex EncabezadoDePaso = new Regex(
            "^(paso\\s*\\d+|\\d+\\s*[.)\\-–])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Corchetes y paréntesis que se quedan vacíos al quitar el enlace de dentro.</summary>
        private static readonly Regex ParejasVacias = new Regex("[\\[\\({]\\s*[\\]\\)}]",
            RegexOptions.Compiled);

        /// <summary>Puntuación que queda suelta y separada de su palabra al limpiar las etiquetas.</summary>
        private static readonly Regex PuntuacionHuerfana = new Regex("\\s+([.,;:!?])",
            RegexOptions.Compiled);

        /// <summary>
        /// El punto que se duplica al pegar la puntuación huérfana a una frase que ya acababa en
        /// punto. Dos puntos seguidos nunca son correctos; tres son unos puntos suspensivos y se
        /// respetan.
        /// </summary>
        private static readonly Regex PuntoDuplicado = new Regex("(?<!\\.)\\.{2}(?!\\.)",
            RegexOptions.Compiled);

        /// <summary>Cada etiqueta se sustituye por un separador, para que dos bloques no se peguen.</summary>
        private static readonly Regex Etiquetas = new Regex("<[^>]+>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex EspaciosSeguidos = new Regex("[ \\t\\r\\n\\f\\v]+",
            RegexOptions.Compiled);

        /// <summary>
        /// Devuelve la descripción de la ficha: la de Nesto si la hay y, si no, un extracto del
        /// protocolo. Devuelve null cuando no hay ni una cosa ni la otra, para que la tienda se
        /// quede con su propio texto de reserva.
        /// </summary>
        public static string Componer(string descripcion, string protocolo)
        {
            if (!string.IsNullOrWhiteSpace(descripcion))
            {
                return descripcion.Trim();
            }

            return ExtractoDeProtocolo(protocolo);
        }

        /// <summary>
        /// Saca el primer párrafo de prosa del protocolo (que es HTML), en texto plano y recortado
        /// por una frontera de palabra.
        /// </summary>
        internal static string ExtractoDeProtocolo(string protocolo)
        {
            if (string.IsNullOrWhiteSpace(protocolo))
            {
                return null;
            }

            string sinAnclas = Anclas.Replace(protocolo, " ");
            string sinEtiquetas = Etiquetas.Replace(sinAnclas, "\n");
            string texto = WebUtility.HtmlDecode(sinEtiquetas);

            string prosa = PrimerTrozoDeProsa(texto);
            return prosa == null ? null : Recortar(prosa, LongitudMaximaExtracto);
        }

        /// <summary>
        /// El primer trozo que sirva como primera frase de la ficha. Si no hay ninguno se devuelve
        /// null a propósito: mejor que la tienda se quede con su texto de reserva que publicar un
        /// encabezado suelto, media frase o el mensaje de error de OpenAI.
        /// </summary>
        private static string PrimerTrozoDeProsa(string texto)
        {
            string encabezadoPrevio = null;

            foreach (string trozoSuelto in texto.Split('\n'))
            {
                string trozo = Limpiar(trozoSuelto);
                if (trozo.Length == 0)
                {
                    continue;
                }

                if (trozo.Length >= LongitudMinimaProsa && EmpiezaComoUnaFrase(trozo) && !EsSentinela(trozo))
                {
                    // Si el primer párrafo de prosa cuelga de un "Paso 2", ya estamos dentro del
                    // procedimiento: lo que venga después está más adentro todavía, así que no se
                    // sigue buscando y la ficha se queda con el texto de reserva de la tienda.
                    return encabezadoPrevio != null && EncabezadoDePaso.IsMatch(encabezadoPrevio)
                        ? null
                        : trozo;
                }

                encabezadoPrevio = trozo;
            }

            return null;
        }

        /// <summary>Deja el trozo en texto plano presentable: sin residuos de las etiquetas quitadas.</summary>
        private static string Limpiar(string trozo)
        {
            string limpio = ParejasVacias.Replace(trozo, " ");
            limpio = EspaciosSeguidos.Replace(limpio, " ");
            limpio = PuntuacionHuerfana.Replace(limpio, "$1");
            limpio = PuntoDuplicado.Replace(limpio, ".");

            // Los dos puntos o el guion con que arranca un trozo son el rastro del encabezado que
            // lo precedía ("Mesoterapia: Este es un término..."), no parte de la frase.
            return limpio.TrimStart(' ', ':', '-', '–', '—', '.', '·', '•').Trim();
        }

        /// <summary>
        /// Una descripción tiene que empezar por el principio de una frase. Los trozos que arrancan
        /// en minúscula son continuaciones de un &lt;li&gt; partido y en la ficha se leen como si
        /// faltara texto.
        /// </summary>
        private static bool EmpiezaComoUnaFrase(string trozo)
        {
            char inicial = trozo[0];
            return char.IsUpper(inicial) || char.IsDigit(inicial) || inicial == '¿' || inicial == '¡';
        }

        private static bool EsSentinela(string trozo)
        {
            foreach (string sentinela in Sentinelas)
            {
                if (trozo.StartsWith(sentinela, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Recorta por la última palabra entera que quepa y remata con puntos suspensivos.</summary>
        private static string Recortar(string texto, int longitudMaxima)
        {
            if (texto.Length <= longitudMaxima)
            {
                return texto;
            }

            int corte = texto.LastIndexOf(' ', longitudMaxima);
            if (corte <= 0)
            {
                corte = longitudMaxima;
            }

            StringBuilder recortado = new StringBuilder(texto.Substring(0, corte).TrimEnd());

            // Si la frase ya moría en un signo de puntuación, no se le añaden puntos suspensivos.
            char ultimo = recortado.Length > 0 ? recortado[recortado.Length - 1] : ' ';
            if (ultimo == '.' || ultimo == '!' || ultimo == '?')
            {
                return recortado.ToString();
            }

            _ = recortado.Append('…');
            return recortado.ToString();
        }
    }
}
