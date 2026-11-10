using System;
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
            using (HttpResponseMessage respuesta = await http.GetAsync(url).ConfigureAwait(false))
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
                byte[] contenido = await respuesta.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                return contenido == null || contenido.Length == 0 || contenido.Length > MAXIMO_BYTES
                    ? null
                    : new DocumentoDescargado { Contenido = contenido, Tipo = tipo };
            }
        }

        /// <summary>PDF o imagen: lo que Nesto sabe enseñar.</summary>
        internal static bool EsDocumentoAdmitido(string tipo)
            => !string.IsNullOrWhiteSpace(tipo) && (tipo == "application/pdf" || tipo.StartsWith("image/", StringComparison.Ordinal));
    }
}
