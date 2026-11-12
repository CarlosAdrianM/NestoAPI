using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
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
        public const string CAMPOS = "id,internetMessageId,receivedDateTime,from,subject,body";
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
            string filtro = "receivedDateTime ge " + desde.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            return $"{URL_GRAPH}/users/{Uri.EscapeDataString(configuracion.Buzon)}/mailFolders/inbox/messages/delta" +
                $"?$select={CAMPOS}&$filter={Uri.EscapeDataString(filtro)}";
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
                CuerpoEsHtml = string.Equals((string)cuerpo?["contentType"], "html", StringComparison.OrdinalIgnoreCase)
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
