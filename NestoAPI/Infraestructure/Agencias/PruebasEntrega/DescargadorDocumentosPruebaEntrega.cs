using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Agencias.PruebasEntrega
{
    /// <summary>Un documento bajado de una agencia: bytes y tipo MIME (sin parámetros, en minúsculas).</summary>
    public class DocumentoDescargado
    {
        public byte[] Contenido { get; set; }
        public string Tipo { get; set; }
    }

    /// <summary>
    /// Prueba de entrega por agencia (10/11/26): baja un justificante o una firma de la URL pública que da la agencia (GLS).
    /// Devuelve null si no es un PDF o una imagen (p. ej. una página HTML): entonces se da el enlace para abrirlo en el navegador.
    /// </summary>
    public interface IDescargadorDocumentosPruebaEntrega
    {
        Task<DocumentoDescargado> DescargarAsync(string url);
    }

    public class DescargadorDocumentosPruebaEntrega : IDescargadorDocumentosPruebaEntrega
    {
        /// <summary>Más que esto no es una prueba de entrega: no se trae al servidor (se da el enlace).</summary>
        internal const int MAXIMO_BYTES = 10 * 1024 * 1024;

        private static readonly HttpClient ClienteCompartido = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly HttpClient http;

        public DescargadorDocumentosPruebaEntrega(HttpClient http = null)
        {
            this.http = http ?? ClienteCompartido;
        }

        public async Task<DocumentoDescargado> DescargarAsync(string url)
        {
            // La URL viene en la respuesta de la agencia: solo se sigue si es de GLS por https (nada de pedir a la red interna).
            if (!EsUrlDeGls(url))
            {
                return null;
            }
            using (HttpResponseMessage respuesta = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                if (!respuesta.IsSuccessStatusCode)
                {
                    throw new AgenciaRemotaException($"La agencia no ha devuelto el justificante (HTTP {(int)respuesta.StatusCode}).");
                }
                string tipo = respuesta.Content?.Headers?.ContentType?.MediaType?.Trim().ToLowerInvariant();
                long? longitud = respuesta.Content?.Headers?.ContentLength;
                if (!EsDocumentoAdmitido(tipo) || longitud > MAXIMO_BYTES)
                {
                    return null;
                }
                byte[] contenido;
                using (Stream cuerpo = await respuesta.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    contenido = await LeerHasta(cuerpo, MAXIMO_BYTES).ConfigureAwait(false);
                }
                return contenido == null || contenido.Length == 0
                    ? null
                    : new DocumentoDescargado { Contenido = contenido, Tipo = tipo };
            }
        }

        /// <summary>Lee el cuerpo sin pasar de <paramref name="maximo"/> bytes; null si es más largo (no se carga entero).</summary>
        internal static async Task<byte[]> LeerHasta(Stream cuerpo, int maximo)
        {
            using (var memoria = new MemoryStream())
            {
                byte[] bufer = new byte[81920];
                int leidos;
                while ((leidos = await cuerpo.ReadAsync(bufer, 0, bufer.Length).ConfigureAwait(false)) > 0)
                {
                    if (memoria.Length + leidos > maximo)
                    {
                        return null;
                    }
                    memoria.Write(bufer, 0, leidos);
                }
                return memoria.ToArray();
            }
        }

        /// <summary>
        /// La URL del justificante que da GLS solo vale si es https y de gls-spain.es o un subdominio suyo (la real es
        /// pods.gls-spain.es). Ni se descarga ni se da al cliente otra cosa.
        /// </summary>
        internal static bool EsUrlDeGls(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri uri))
            {
                return false;
            }
            if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            {
                return false;
            }
            string host = uri.IdnHost?.ToLowerInvariant();
            return host == DOMINIO_GLS || (host != null && host.EndsWith("." + DOMINIO_GLS, StringComparison.Ordinal));
        }

        private const string DOMINIO_GLS = "gls-spain.es";

        /// <summary>PDF o imagen: lo que Nesto sabe enseñar.</summary>
        internal static bool EsDocumentoAdmitido(string tipo)
            => !string.IsNullOrWhiteSpace(tipo) && (tipo == "application/pdf" || tipo.StartsWith("image/", StringComparison.Ordinal));
    }
}
