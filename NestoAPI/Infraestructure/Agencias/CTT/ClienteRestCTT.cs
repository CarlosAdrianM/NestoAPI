using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NestoAPI.Infraestructure.Agencias.CTT
{
    /// <summary>
    /// Respuesta HTTP de la API de CTT ya leída. Los errores de negocio (4xx) NO lanzan: vienen aquí
    /// con <see cref="Exito"/> a false y el motivo en <see cref="Error"/>, para que la agencia decida
    /// (p. ej. "ya anulado" es un éxito idempotente). Los fallos de transporte y los 5xx sí lanzan
    /// <see cref="AgenciaRemotaException"/> (y los transitorios se reintentan en la política común).
    /// </summary>
    public class RespuestaCTT
    {
        public int Codigo { get; set; }
        public string Cuerpo { get; set; }
        public bool Exito => Codigo >= 200 && Codigo <= 299;

        private JToken _json;
        private bool _parseado;

        /// <summary>El cuerpo como JSON, o null si está vacío o no es JSON.</summary>
        public JToken Json
        {
            get
            {
                if (!_parseado)
                {
                    _parseado = true;
                    try
                    {
                        // DateParseHandling.None: las fechas ISO de CTT ("2026-09-18T07:49:31.565+00:00")
                        // se quedan como texto. Si Json.NET las convirtiera a DateTime, ToString() daría
                        // "18/09/2026 9:49:31" (cultura local) y ordenar eventos por fecha se rompería.
                        if (string.IsNullOrWhiteSpace(Cuerpo))
                        {
                            _json = null;
                        }
                        else
                        {
                            using (var lector = new JsonTextReader(new System.IO.StringReader(Cuerpo)) { DateParseHandling = DateParseHandling.None })
                            {
                                _json = JToken.ReadFrom(lector);
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        _json = null;
                    }
                }
                return _json;
            }
        }

        /// <summary>
        /// Clave de traducción del error de CTT (p. ej. "ALREADY_NULLED_SHIPPING"), o null.
        /// </summary>
        public string ClaveError => (ErrorComoObjeto)?["translation_key"]?.ToString();

        /// <summary>
        /// El nodo "error" del cuerpo SOLO si es un objeto. CTT no siempre lo manda así: el
        /// seguimiento devolvió <c>{"error":"texto"}</c> (22/09/26, 12:46, primer día en producción) y
        /// el <c>error["x"]</c> sobre un JValue lanzaba InvalidOperationException y tapaba el motivo real.
        /// </summary>
        private JObject ErrorComoObjeto => (Json as JObject)?["error"] as JObject;

        /// <summary>
        /// Texto legible del error: el mensaje extendido de CTT si lo hay, si no su descripción, si no
        /// el "error" plano, si no el cuerpo tal cual (acotado). Nunca null cuando no hay éxito y
        /// nunca lanza: sea cual sea la forma del cuerpo, el motivo llega al log.
        /// </summary>
        public string Error
        {
            get
            {
                if (Exito) return null;
                JObject error = ErrorComoObjeto;
                string mensaje = (error?["error_extended_info"] as JObject)?["message"]?.ToString();
                string descripcion = error?["error_description"]?.ToString();
                JToken errorPlano = (Json as JObject)?["error"];
                string plano = errorPlano is JValue ? errorPlano.ToString() : null;
                string detalle = !string.IsNullOrWhiteSpace(mensaje) ? mensaje
                    : !string.IsNullOrWhiteSpace(descripcion) ? descripcion
                    : !string.IsNullOrWhiteSpace(plano) ? plano
                    : (Cuerpo ?? string.Empty).Trim();
                if (detalle.Length > 300) detalle = detalle.Substring(0, 300) + "…";
                return $"CTT respondió {Codigo}{(string.IsNullOrEmpty(detalle) ? string.Empty : ": " + detalle)}";
            }
        }
    }

    public interface IClienteRestCTT
    {
        /// <summary>
        /// Ejecuta una llamada autenticada (Bearer) contra la API de CTT. <paramref name="rutaRelativa"/>
        /// cuelga de la URL base (p. ej. "manifest/v2.0/shippings"). <paramref name="cuerpo"/> se
        /// serializa a JSON (null = sin cuerpo). <paramref name="operacion"/> es el nombre con el que
        /// queda en la auditoría de intercambios.
        /// </summary>
        Task<RespuestaCTT> EnviarAsync(HttpMethod metodo, string rutaRelativa, object cuerpo, string operacion);
    }

    /// <summary>
    /// Token OAuth2 (client_credentials) de CTT, compartido por proceso y por credencial. CTT pide
    /// expresamente UN token al día: dura 24 h y con él se hacen todas las llamadas. Se renueva
    /// solo cuando quedan menos de <see cref="MARGEN_RENOVACION"/> para caducar, o cuando la API
    /// responde 401 (<see cref="Invalidar"/>).
    /// </summary>
    public static class ProveedorTokenCTT
    {
        private class TokenCacheado
        {
            public string Token;
            public DateTime ExpiraUtc;
        }

        private static readonly ConcurrentDictionary<string, TokenCacheado> _tokens = new ConcurrentDictionary<string, TokenCacheado>();
        private static readonly TimeSpan MARGEN_RENOVACION = TimeSpan.FromMinutes(10);

        public static async Task<string> ObtenerAsync(ConfiguracionCTT config, HttpClient http, RegistroIntercambiosRemotos registro)
        {
            string clave = config.UrlBase + "|" + config.ClientId;
            if (_tokens.TryGetValue(clave, out TokenCacheado cacheado) && cacheado.ExpiraUtc - DateTime.UtcNow > MARGEN_RENOVACION)
            {
                return cacheado.Token;
            }

            if (!config.TieneCredenciales)
            {
                throw new AgenciaRemotaException("CTT: faltan las credenciales (CTTClientId / CTTClientSecret en secretos.config).");
            }

            string url = config.UrlBase + "oauth2/token";
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", config.ClientId),
                new KeyValuePair<string, string>("client_secret", config.ClientSecret),
                new KeyValuePair<string, string>("scope", ConfiguracionCTT.SCOPE),
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            HttpResponseMessage respuesta;
            string cuerpo;
            try
            {
                respuesta = await http.PostAsync(url, form).ConfigureAwait(false);
                cuerpo = await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                registro?.Registrar("Token", url, "(credenciales)", "ERROR: " + ex.Message);
                throw new AgenciaRemotaException("CTT: no se pudo conectar para obtener el token: " + ex.Message, ex) { EsTransitoria = true };
            }

            if (!respuesta.IsSuccessStatusCode)
            {
                registro?.Registrar("Token", url, "(credenciales)", $"HTTP {(int)respuesta.StatusCode}: {Acotar(cuerpo)}");
                throw new AgenciaRemotaException($"CTT: no se pudo obtener el token (HTTP {(int)respuesta.StatusCode}): {Acotar(cuerpo)}")
                {
                    EsTransitoria = (int)respuesta.StatusCode >= 500
                };
            }

            JObject json;
            try
            {
                json = JObject.Parse(cuerpo);
            }
            catch (JsonException ex)
            {
                throw new AgenciaRemotaException("CTT: la respuesta del token no es JSON: " + Acotar(cuerpo), ex);
            }
            string token = json["access_token"]?.ToString();
            int expiraEn = json["expires_in"]?.Value<int>() ?? 86400;
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new AgenciaRemotaException("CTT: la respuesta del token no trae access_token.");
            }

            registro?.Registrar("Token", url, "(credenciales)", $"token obtenido, caduca en {expiraEn} s");
            _tokens[clave] = new TokenCacheado { Token = token, ExpiraUtc = DateTime.UtcNow.AddSeconds(expiraEn) };
            return token;
        }

        public static void Invalidar(ConfiguracionCTT config)
        {
            _tokens.TryRemove(config.UrlBase + "|" + config.ClientId, out _);
        }

        private static string Acotar(string texto)
        {
            texto = (texto ?? string.Empty).Trim();
            return texto.Length > 300 ? texto.Substring(0, 300) + "…" : texto;
        }
    }

    /// <summary>
    /// Cliente HTTP de la API REST de CTT Express (NestoAPI#493): pone el Bearer, serializa el JSON,
    /// registra cada intercambio (petición y respuesta crudas, sin el token) y traduce los fallos de
    /// transporte a <see cref="AgenciaRemotaException"/>. Un 401 invalida el token cacheado y
    /// reintenta UNA vez con token nuevo.
    /// </summary>
    public class ClienteRestCTT : IClienteRestCTT
    {
        private static readonly HttpClient HttpCompartido = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        private readonly ConfiguracionCTT _config;
        private readonly HttpClient _http;
        private readonly RegistroIntercambiosRemotos _registro;

        public ClienteRestCTT(ConfiguracionCTT config, HttpClient http = null, RegistroIntercambiosRemotos registro = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _http = http ?? HttpCompartido;
            _registro = registro;
        }

        public async Task<RespuestaCTT> EnviarAsync(HttpMethod metodo, string rutaRelativa, object cuerpo, string operacion)
        {
            string token = await ProveedorTokenCTT.ObtenerAsync(_config, _http, _registro).ConfigureAwait(false);
            RespuestaCTT respuesta = await EnviarConTokenAsync(metodo, rutaRelativa, cuerpo, operacion, token).ConfigureAwait(false);
            if (respuesta.Codigo == (int)HttpStatusCode.Unauthorized)
            {
                // Token revocado o caducado antes de tiempo: uno nuevo y una única repetición.
                ProveedorTokenCTT.Invalidar(_config);
                token = await ProveedorTokenCTT.ObtenerAsync(_config, _http, _registro).ConfigureAwait(false);
                respuesta = await EnviarConTokenAsync(metodo, rutaRelativa, cuerpo, operacion, token).ConfigureAwait(false);
            }
            return respuesta;
        }

        private async Task<RespuestaCTT> EnviarConTokenAsync(HttpMethod metodo, string rutaRelativa, object cuerpo, string operacion, string token)
        {
            string url = _config.UrlBase + rutaRelativa.TrimStart('/');
            string peticionJson = cuerpo == null ? string.Empty : JsonConvert.SerializeObject(cuerpo, Formatting.None);

            var request = new HttpRequestMessage(metodo, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (cuerpo != null)
            {
                request.Content = new StringContent(peticionJson, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage http;
            string cuerpoRespuesta;
            try
            {
                http = await _http.SendAsync(request).ConfigureAwait(false);
                cuerpoRespuesta = http.Content == null ? string.Empty : await http.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _registro?.Registrar(operacion, url, peticionJson, "ERROR: " + ex.Message);
                throw new AgenciaRemotaException($"CTT ({operacion}): no se pudo conectar: {ex.Message}", ex) { EsTransitoria = true };
            }
            catch (TaskCanceledException ex)
            {
                _registro?.Registrar(operacion, url, peticionJson, "ERROR: timeout");
                throw new AgenciaRemotaException($"CTT ({operacion}): la petición agotó el tiempo de espera.", ex) { EsTransitoria = true };
            }

            int codigo = (int)http.StatusCode;
            _registro?.Registrar(operacion, url, peticionJson, $"HTTP {codigo}: {cuerpoRespuesta}");

            if (codigo >= 500)
            {
                throw new AgenciaRemotaException($"CTT ({operacion}) respondió HTTP {codigo}: {Acotar(cuerpoRespuesta)}") { EsTransitoria = true };
            }

            return new RespuestaCTT { Codigo = codigo, Cuerpo = cuerpoRespuesta };
        }

        private static string Acotar(string texto)
        {
            texto = (texto ?? string.Empty).Trim();
            return texto.Length > 300 ? texto.Substring(0, 300) + "…" : texto;
        }
    }
}
