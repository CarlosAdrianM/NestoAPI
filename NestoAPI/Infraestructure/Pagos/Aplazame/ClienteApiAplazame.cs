using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos.Aplazame
{
    /// <summary>
    /// NestoAPI#663: lo que se le pide a la API REST de Aplázame. Detrás de una interfaz para probar sin red.
    /// Todos los importes van en CÉNTIMOS (los «decimales» de Aplázame son el importe × 100).
    /// </summary>
    public interface IClienteApiAplazame
    {
        /// <summary>POST /checkout (API v4). Devuelve el id del checkout, que es lo que abre aplazame.js.</summary>
        Task<string> CrearCheckout(CheckoutAplazame checkout, bool sandbox);

        /// <summary>GET /orders/{mid}/captures (API v1): lo capturado y lo que queda por capturar.</summary>
        Task<CapturasAplazame> ConsultarCapturas(string mid, bool sandbox);

        /// <summary>POST /orders/{mid}/captures (API v1).</summary>
        Task Capturar(string mid, long centimos, bool sandbox);

        /// <summary>POST /orders/{mid}/cancel (API v1): solo para pedidos confirmados y sin capturar.</summary>
        Task Cancelar(string mid, bool sandbox);
    }

    /// <summary>Respuesta de GET /orders/{mid}/captures.</summary>
    public class CapturasAplazame
    {
        [JsonProperty("remaining_capture_amount")]
        public long PendienteDeCapturar { get; set; }

        [JsonProperty("results")]
        public List<CapturaAplazame> Capturas { get; set; } = new List<CapturaAplazame>();
    }

    public class CapturaAplazame
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("amount")]
        public long Importe { get; set; }

        [JsonProperty("created")]
        public DateTimeOffset? Fecha { get; set; }
    }

    /// <summary>Aplázame ha contestado con un error (4xx/5xx). Lleva el cuerpo para el ELMAH.</summary>
    public class AplazameApiException : Exception
    {
        public AplazameApiException(HttpStatusCode estado, string cuerpo, string operacion)
            : base($"Aplázame ha respondido {(int)estado} {estado} a {operacion}: {Recortar(cuerpo)}")
        {
            Estado = estado;
            Cuerpo = cuerpo;
        }

        public HttpStatusCode Estado { get; }
        public string Cuerpo { get; }

        private static string Recortar(string texto) => texto == null ? string.Empty : texto.Length > 1000 ? texto.Substring(0, 1000) : texto;
    }

    /// <summary>
    /// NestoAPI#663: cliente HTTP mínimo, equivalente al SDK oficial de .NET (aplazame/dotnet-sdk, licencia BSD-3):
    /// <c>Authorization: Bearer &lt;clave privada&gt;</c> y <c>Accept: application/vnd.aplazame[.sandbox].vN+json</c>. El
    /// sandbox es SOLO esa cabecera: las claves son las mismas.
    /// </summary>
    public class ClienteApiAplazame : IClienteApiAplazame
    {
        internal const string URL_BASE = "https://api.aplazame.com";
        internal const int VERSION_CHECKOUT = 4;
        internal const int VERSION_PEDIDOS = 1;

        // Uno para toda la aplicación: crear un HttpClient por llamada agota los sockets
        private static readonly HttpClient Http = new HttpClient { BaseAddress = new Uri(URL_BASE), Timeout = TimeSpan.FromSeconds(30) };

        private readonly string clavePrivada;

        public ClienteApiAplazame(string clavePrivada)
        {
            if (string.IsNullOrWhiteSpace(clavePrivada))
            {
                throw new InvalidOperationException("Falta la clave privada de Aplázame (AplazamePrivateKey en secretos.config)");
            }
            this.clavePrivada = clavePrivada.Trim();
        }

        internal static string CabeceraAccept(bool sandbox, int version)
        {
            return "application/vnd.aplazame" + (sandbox ? ".sandbox" : string.Empty) + $".v{version}+json";
        }

        public async Task<string> CrearCheckout(CheckoutAplazame checkout, bool sandbox)
        {
            string respuesta = await Enviar(HttpMethod.Post, "/checkout", checkout, sandbox, VERSION_CHECKOUT, "crear el checkout")
                .ConfigureAwait(false);
            string id = JObject.Parse(respuesta)["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new AplazameApiException(HttpStatusCode.OK, respuesta, "crear el checkout (sin id)");
            }
            return id;
        }

        public async Task<CapturasAplazame> ConsultarCapturas(string mid, bool sandbox)
        {
            string respuesta = await Enviar(HttpMethod.Get, $"/orders/{Uri.EscapeDataString(mid)}/captures", null, sandbox,
                VERSION_PEDIDOS, $"consultar las capturas de {mid}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<CapturasAplazame>(respuesta) ?? new CapturasAplazame();
        }

        public Task Capturar(string mid, long centimos, bool sandbox)
        {
            return Enviar(HttpMethod.Post, $"/orders/{Uri.EscapeDataString(mid)}/captures", new { amount = centimos }, sandbox,
                VERSION_PEDIDOS, $"capturar {centimos} céntimos de {mid}");
        }

        public Task Cancelar(string mid, bool sandbox)
        {
            return Enviar(HttpMethod.Post, $"/orders/{Uri.EscapeDataString(mid)}/cancel", new { }, sandbox,
                VERSION_PEDIDOS, $"cancelar {mid}");
        }

        private async Task<string> Enviar(HttpMethod metodo, string ruta, object cuerpo, bool sandbox, int version, string operacion)
        {
            using (HttpRequestMessage peticion = new HttpRequestMessage(metodo, ruta))
            {
                peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clavePrivada);
                _ = peticion.Headers.TryAddWithoutValidation("Accept", CabeceraAccept(sandbox, version));
                _ = peticion.Headers.TryAddWithoutValidation("User-Agent", "NestoAPI/1.0 (Nueva Vision)");
                if (cuerpo != null)
                {
                    string json = JsonConvert.SerializeObject(cuerpo, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                    peticion.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                using (HttpResponseMessage respuesta = await Http.SendAsync(peticion).ConfigureAwait(false))
                {
                    string texto = respuesta.Content == null ? string.Empty : await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!respuesta.IsSuccessStatusCode)
                    {
                        throw new AplazameApiException(respuesta.StatusCode, texto, operacion);
                    }
                    return texto;
                }
            }
        }
    }
}
