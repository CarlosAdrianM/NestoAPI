using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>
    /// NestoAPI#857: las claves del buzón de Compras en secretos.config (aplicación de Entra con permiso de APLICACIÓN Mail.Read,
    /// limitada al buzón con New-ApplicationAccessPolicy). Si falta alguna, no se lee nada.
    /// </summary>
    public class ConfiguracionBuzonCompras
    {
        public const string CLAVE_TENANT = "ComprasBuzonTenant";
        public const string CLAVE_CLIENT_ID = "ComprasBuzonClientId";
        public const string CLAVE_SECRETO = "ComprasBuzonSecreto";
        public const string CLAVE_BUZON = "ComprasBuzon";

        public string Tenant { get; set; }
        public string ClientId { get; set; }
        public string Secreto { get; set; }
        public string Buzon { get; set; }

        /// <summary>Null si falta alguna de las cuatro claves.</summary>
        public static ConfiguracionBuzonCompras Leer(Func<string, string> configuracion)
        {
            if (configuracion == null)
            {
                return null;
            }
            var config = new ConfiguracionBuzonCompras
            {
                Tenant = configuracion(CLAVE_TENANT)?.Trim(),
                ClientId = configuracion(CLAVE_CLIENT_ID)?.Trim(),
                Secreto = configuracion(CLAVE_SECRETO)?.Trim(),
                Buzon = configuracion(CLAVE_BUZON)?.Trim()
            };
            return string.IsNullOrEmpty(config.Tenant) || string.IsNullOrEmpty(config.ClientId) || string.IsNullOrEmpty(config.Secreto)
                || string.IsNullOrEmpty(config.Buzon)
                ? null
                : config;
        }
    }

    /// <summary>Lo que ha traído una lectura del buzón.</summary>
    public class LecturaBuzonCompras
    {
        public List<CorreoCompras> Correos { get; set; } = new List<CorreoCompras>();
        /// <summary>
        /// Por dónde seguir en la próxima pasada (deltaLink si se ha llegado al final, nextLink si se ha cortado por páginas o por un
        /// error a mitad). Null = no cambiar lo guardado.
        /// </summary>
        public string SiguienteEnlace { get; set; }
        /// <summary>El estado del delta ya no vale (410): hay que borrar lo guardado y empezar de nuevo.</summary>
        public bool Reiniciar { get; set; }
        /// <summary>Se ha llegado al deltaLink (no queda nada por leer).</summary>
        public bool Completa { get; set; }
        /// <summary>Texto del fallo (401, 429, red…); null si ha ido bien.</summary>
        public string Error { get; set; }
        public HttpStatusCode? Estado { get; set; }
        /// <summary>Lo que Graph pide esperar (cabecera Retry-After de un 429/503); null si no lo dice.</summary>
        public TimeSpan? EsperarAntesDeReintentar { get; set; }
    }

    /// <summary>NestoAPI#857: un adjunto de un correo, sin su contenido (nunca se descarga).</summary>
    public class AdjuntoCorreo
    {
        public string Nombre { get; set; }
        public string Tipo { get; set; }
        public long Tamano { get; set; }
        /// <summary>Imagen incrustada (la de la firma): no cuenta como adjunto.</summary>
        public bool EnLinea { get; set; }
    }

    /// <summary>NestoAPI#857: lectura de SOLO LECTURA de la bandeja de entrada del buzón de Compras (nunca marca como leído ni mueve).</summary>
    public interface ILectorBuzonCompras
    {
        string Buzon { get; }
        /// <summary>
        /// Lo nuevo desde <paramref name="enlaceGuardado"/> (el deltaLink/nextLink de la pasada anterior). Sin enlace, empieza por los
        /// recibidos desde <paramref name="desdeSiNoHayEnlace"/> (no se lee el histórico del buzón entero). Nunca lanza.
        /// </summary>
        Task<LecturaBuzonCompras> LeerNuevos(string enlaceGuardado, DateTime desdeSiNoHayEnlace);

        /// <summary>
        /// Lectura HACIA ATRÁS (histórico): UNA página de la bandeja de entrada con <c>receivedDateTime</c> en [desde, hasta), de la
        /// más antigua a la más nueva. Sin <paramref name="enlace"/>, la primera; con él, el nextLink de la anterior. En la respuesta,
        /// SiguienteEnlace = la página siguiente (null si no hay más). No usa ni toca el delta. Nunca lanza.
        /// </summary>
        Task<LecturaBuzonCompras> LeerPagina(string enlace, DateTime desde, DateTime hasta);

        /// <summary>Nombre, tipo y tamaño de los adjuntos de un mensaje (sin el contenido). Null si no se han podido leer. Nunca lanza.</summary>
        Task<List<AdjuntoCorreo>> LeerAdjuntos(string idGraph);
    }

    /// <summary>
    /// NestoAPI#857: Microsoft Graph por HttpClient (sin SDK): token de aplicación (client credentials) y consulta incremental
    /// (delta) de <c>/users/{buzón}/mailFolders/inbox/messages/delta</c>. Se pide el cuerpo en la misma consulta porque los enlaces
    /// están en el HTML (bodyPreview son 255 caracteres de texto y no los trae); el cuerpo no se guarda.
    /// </summary>
    public class LectorBuzonComprasGraph : ILectorBuzonCompras
    {
        public const string URL_GRAPH = "https://graph.microsoft.com/v1.0";
        public const string SCOPE = "https://graph.microsoft.com/.default";
        public const string CAMPOS = "id,internetMessageId,receivedDateTime,from,subject,body,hasAttachments";
        public const string CAMPOS_ADJUNTOS = "name,contentType,size,isInline";
        public const int TAMANO_PAGINA = 50;
        /// <summary>Tope de páginas por pasada; lo que quede se lee en la siguiente (se guarda el nextLink).</summary>
        public const int MAXIMO_PAGINAS = 20;

        private static readonly HttpClient httpPorDefecto = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        private readonly ConfiguracionBuzonCompras configuracion;
        private readonly HttpClient http;
        private readonly Func<DateTime> ahoraUtc;
        private string token;
        private DateTime caducaUtc;

        public LectorBuzonComprasGraph(ConfiguracionBuzonCompras configuracion, HttpClient http = null, Func<DateTime> ahoraUtc = null)
        {
            this.configuracion = configuracion ?? throw new ArgumentNullException(nameof(configuracion));
            this.http = http ?? httpPorDefecto;
            this.ahoraUtc = ahoraUtc ?? (() => DateTime.UtcNow);
        }

        public string Buzon => configuracion.Buzon;

        internal string UrlInicial(DateTime desde)
        {
            string filtro = "receivedDateTime ge " + FechaGraph(desde);
            return $"{URL_GRAPH}/users/{Uri.EscapeDataString(configuracion.Buzon)}/mailFolders/inbox/messages/delta" +
                $"?$select={CAMPOS}&$filter={Uri.EscapeDataString(filtro)}";
        }

        private static string FechaGraph(DateTime fecha) =>
            fecha.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        /// <summary>NestoAPI#857 (histórico): la bandeja por fechas, de la más antigua a la más nueva (sin delta).</summary>
        internal string UrlHistorico(DateTime desde, DateTime hasta)
        {
            string filtro = $"receivedDateTime ge {FechaGraph(desde)} and receivedDateTime lt {FechaGraph(hasta)}";
            return $"{URL_GRAPH}/users/{Uri.EscapeDataString(configuracion.Buzon)}/mailFolders/inbox/messages" +
                $"?$select={CAMPOS}&$filter={Uri.EscapeDataString(filtro)}&$orderby={Uri.EscapeDataString("receivedDateTime asc")}&$top={TAMANO_PAGINA}";
        }

        internal string UrlAdjuntos(string idGraph) =>
            $"{URL_GRAPH}/users/{Uri.EscapeDataString(configuracion.Buzon)}/messages/{Uri.EscapeDataString(idGraph)}/attachments" +
            $"?$select={CAMPOS_ADJUNTOS}&$top=50";

        private class RespuestaGraph
        {
            public JObject Json { get; set; }
            public HttpStatusCode Estado { get; set; }
            public string Error { get; set; }
            public TimeSpan? Esperar { get; set; }
        }

        /// <summary>Un GET a Graph (solo lectura). Los fallos de HTTP van en Error; los de red lanzan.</summary>
        private async Task<RespuestaGraph> Get(string url, string bearer)
        {
            using (var peticion = new HttpRequestMessage(HttpMethod.Get, url))
            {
                peticion.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
                peticion.Headers.Add("Prefer", "odata.maxpagesize=" + TAMANO_PAGINA);
                using (HttpResponseMessage respuesta = await http.SendAsync(peticion).ConfigureAwait(false))
                {
                    string cuerpo = respuesta.Content == null ? null : await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!respuesta.IsSuccessStatusCode)
                    {
                        if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            token = null;
                        }
                        return new RespuestaGraph
                        {
                            Estado = respuesta.StatusCode,
                            Error = $"Graph ha contestado {(int)respuesta.StatusCode} ({respuesta.StatusCode}): {Recortar(cuerpo, 300)}",
                            Esperar = RetryAfter(respuesta)
                        };
                    }
                    return new RespuestaGraph { Estado = respuesta.StatusCode, Json = JObject.Parse(cuerpo ?? "{}") };
                }
            }
        }

        internal static TimeSpan? RetryAfter(HttpResponseMessage respuesta)
        {
            System.Net.Http.Headers.RetryConditionHeaderValue retry = respuesta?.Headers?.RetryAfter;
            if (retry == null)
            {
                return null;
            }
            if (retry.Delta.HasValue)
            {
                return retry.Delta.Value;
            }
            if (retry.Date.HasValue)
            {
                TimeSpan falta = retry.Date.Value.UtcDateTime - DateTime.UtcNow;
                return falta > TimeSpan.Zero ? falta : TimeSpan.Zero;
            }
            return null;
        }

        public async Task<LecturaBuzonCompras> LeerPagina(string enlace, DateTime desde, DateTime hasta)
        {
            var lectura = new LecturaBuzonCompras();
            try
            {
                string bearer;
                try
                {
                    bearer = await Token().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    lectura.Error = "No se ha podido obtener el token de Graph: " + ex.Message;
                    return lectura;
                }
                RespuestaGraph respuesta = await Get(string.IsNullOrWhiteSpace(enlace) ? UrlHistorico(desde, hasta) : enlace.Trim(), bearer)
                    .ConfigureAwait(false);
                if (respuesta.Error != null)
                {
                    lectura.Estado = respuesta.Estado;
                    lectura.Error = respuesta.Error;
                    lectura.EsperarAntesDeReintentar = respuesta.Esperar;
                    return lectura;
                }
                if (respuesta.Json["value"] is JArray mensajes)
                {
                    foreach (JToken mensaje in mensajes)
                    {
                        CorreoCompras correo = Convertir(mensaje);
                        if (correo != null)
                        {
                            lectura.Correos.Add(correo);
                        }
                    }
                }
                lectura.SiguienteEnlace = (string)respuesta.Json["@odata.nextLink"];
                lectura.Completa = string.IsNullOrEmpty(lectura.SiguienteEnlace);
                return lectura;
            }
            catch (Exception ex)
            {
                lectura.Error = "No se ha podido leer el buzón: " + ex.Message;
                return lectura;
            }
        }

        public async Task<List<AdjuntoCorreo>> LeerAdjuntos(string idGraph)
        {
            if (string.IsNullOrWhiteSpace(idGraph))
            {
                return null;
            }
            try
            {
                string bearer = await Token().ConfigureAwait(false);
                RespuestaGraph respuesta = await Get(UrlAdjuntos(idGraph.Trim()), bearer).ConfigureAwait(false);
                if (respuesta.Error != null || !(respuesta.Json["value"] is JArray adjuntos))
                {
                    return null;
                }
                return adjuntos.Select(a => new AdjuntoCorreo
                {
                    Nombre = (string)a["name"],
                    Tipo = (string)a["contentType"],
                    Tamano = (long?)a["size"] ?? 0,
                    EnLinea = (bool?)a["isInline"] ?? false
                }).ToList();
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task<LecturaBuzonCompras> LeerNuevos(string enlaceGuardado, DateTime desdeSiNoHayEnlace)
        {
            var lectura = new LecturaBuzonCompras();
            string bearer;
            try
            {
                bearer = await Token().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lectura.Error = "No se ha podido obtener el token de Graph: " + ex.Message;
                return lectura;
            }

            string inicial = string.IsNullOrWhiteSpace(enlaceGuardado) ? UrlInicial(desdeSiNoHayEnlace) : enlaceGuardado.Trim();
            string actual = inicial;
            for (int pagina = 0; pagina < MAXIMO_PAGINAS; pagina++)
            {
                JObject json;
                try
                {
                    using (var peticion = new HttpRequestMessage(HttpMethod.Get, actual))
                    {
                        peticion.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
                        peticion.Headers.Add("Prefer", "odata.maxpagesize=" + TAMANO_PAGINA);
                        using (HttpResponseMessage respuesta = await http.SendAsync(peticion).ConfigureAwait(false))
                        {
                            string cuerpo = respuesta.Content == null ? null : await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if (!respuesta.IsSuccessStatusCode)
                            {
                                lectura.Estado = respuesta.StatusCode;
                                lectura.Error = $"Graph ha contestado {(int)respuesta.StatusCode} ({respuesta.StatusCode}): {Recortar(cuerpo, 300)}";
                                if (respuesta.StatusCode == HttpStatusCode.Gone)
                                {
                                    lectura.Reiniciar = true;
                                }
                                else if (pagina > 0)
                                {
                                    // Lo leído hasta aquí vale: la próxima pasada reintenta esta página
                                    lectura.SiguienteEnlace = actual;
                                }
                                if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
                                {
                                    token = null;
                                }
                                return lectura;
                            }
                            json = JObject.Parse(cuerpo ?? "{}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    lectura.Error = "No se ha podido leer el buzón: " + ex.Message;
                    if (pagina > 0)
                    {
                        lectura.SiguienteEnlace = actual;
                    }
                    return lectura;
                }

                if (json["value"] is JArray mensajes)
                {
                    foreach (JToken mensaje in mensajes)
                    {
                        CorreoCompras correo = Convertir(mensaje);
                        if (correo != null)
                        {
                            lectura.Correos.Add(correo);
                        }
                    }
                }
                string delta = (string)json["@odata.deltaLink"];
                string siguiente = (string)json["@odata.nextLink"];
                if (!string.IsNullOrEmpty(delta))
                {
                    lectura.SiguienteEnlace = delta;
                    lectura.Completa = true;
                    return lectura;
                }
                if (string.IsNullOrEmpty(siguiente))
                {
                    // Respuesta rara (ni nextLink ni deltaLink): se deja lo guardado como estaba
                    lectura.Error = "Graph no ha devuelto ni @odata.nextLink ni @odata.deltaLink.";
                    return lectura;
                }
                actual = siguiente;
            }
            lectura.SiguienteEnlace = actual;
            return lectura;
        }

        internal static CorreoCompras Convertir(JToken mensaje)
        {
            if (mensaje == null || mensaje["@removed"] != null)
            {
                return null;
            }
            DateTime recibido = DateTime.Now;
            JToken fecha = mensaje["receivedDateTime"];
            if (fecha != null && fecha.Type == JTokenType.Date)
            {
                recibido = ((DateTime)fecha).ToUniversalTime().ToLocalTime();
            }
            else if (fecha != null && DateTimeOffset.TryParse((string)fecha, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset dto))
            {
                recibido = dto.UtcDateTime.ToLocalTime();
            }
            JToken direccion = mensaje["from"]?["emailAddress"];
            JToken cuerpo = mensaje["body"];
            return new CorreoCompras
            {
                Id = (string)mensaje["id"],
                MensajeId = (string)mensaje["internetMessageId"],
                Recibido = recibido,
                RemitenteDireccion = ((string)direccion?["address"])?.Trim(),
                RemitenteNombre = ((string)direccion?["name"])?.Trim(),
                Asunto = (string)mensaje["subject"],
                Cuerpo = (string)cuerpo?["content"],
                CuerpoEsHtml = string.Equals((string)cuerpo?["contentType"], "html", StringComparison.OrdinalIgnoreCase),
                TieneAdjuntos = mensaje["hasAttachments"]?.Type == JTokenType.Boolean && (bool)mensaje["hasAttachments"]
            };
        }

        private async Task<string> Token()
        {
            if (token != null && ahoraUtc() < caducaUtc)
            {
                return token;
            }
            string url = $"https://login.microsoftonline.com/{Uri.EscapeDataString(configuracion.Tenant)}/oauth2/v2.0/token";
            using (var contenido = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = configuracion.ClientId,
                ["client_secret"] = configuracion.Secreto,
                ["scope"] = SCOPE,
                ["grant_type"] = "client_credentials"
            }))
            using (HttpResponseMessage respuesta = await http.PostAsync(url, contenido).ConfigureAwait(false))
            {
                string cuerpo = respuesta.Content == null ? null : await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!respuesta.IsSuccessStatusCode)
                {
                    // El cuerpo de error de Entra no lleva el secreto (solo el código AADSTS y la descripción)
                    throw new InvalidOperationException($"{(int)respuesta.StatusCode} {Recortar(cuerpo, 300)}");
                }
                JObject json = JObject.Parse(cuerpo ?? "{}");
                string nuevo = (string)json["access_token"];
                if (string.IsNullOrEmpty(nuevo))
                {
                    throw new InvalidOperationException("La respuesta no trae access_token.");
                }
                int segundos = (int?)json["expires_in"] ?? 3600;
                token = nuevo;
                caducaUtc = ahoraUtc().AddSeconds(Math.Max(60, segundos - 300));
                return token;
            }
        }

        private static string Recortar(string texto, int maximo) =>
            string.IsNullOrEmpty(texto) ? string.Empty : texto.Length <= maximo ? texto : texto.Substring(0, maximo);
    }
}
