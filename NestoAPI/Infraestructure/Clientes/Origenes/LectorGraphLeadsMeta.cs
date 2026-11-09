using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes.Origenes
{
    /// <summary>NestoAPI#623 corte 5c: la respuesta HTTP de la API Graph de Meta, tal cual.</summary>
    public class RespuestaGraphMeta
    {
        public int Estado { get; set; }
        public string Cuerpo { get; set; }
    }

    /// <summary>NestoAPI#623 corte 5c: el GET a la API Graph (sustituible en los tests). El token va en la cabecera, nunca en la URL.</summary>
    public interface IClienteGraphMeta
    {
        Task<RespuestaGraphMeta> Get(string url, string token);
    }

    public class ClienteGraphMeta : IClienteGraphMeta
    {
        private static readonly HttpClient cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        public async Task<RespuestaGraphMeta> Get(string url, string token)
        {
            using (var peticion = new HttpRequestMessage(HttpMethod.Get, url))
            {
                peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using (HttpResponseMessage respuesta = await cliente.SendAsync(peticion).ConfigureAwait(false))
                {
                    return new RespuestaGraphMeta
                    {
                        Estado = (int)respuesta.StatusCode,
                        Cuerpo = respuesta.Content == null ? null : await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false)
                    };
                }
            }
        }
    }

    public enum TipoErrorGraphMeta
    {
        /// <summary>Token caducado, revocado o no válido (código 190, 102…): hay que generar otro.</summary>
        Token,
        /// <summary>Al token le falta un permiso o la página/formulario no está asignado (10, 200-299).</summary>
        Permiso,
        /// <summary>Límite de llamadas (4, 17, 32, 613): se pasa solo.</summary>
        Limite,
        /// <summary>Red, 5xx o algo que no se entiende.</summary>
        Otro
    }

    /// <summary>NestoAPI#623 corte 5c: un error de la API Graph, clasificado. El mensaje nunca lleva el token.</summary>
    public class ErrorGraphMetaException : Exception
    {
        public TipoErrorGraphMeta Tipo { get; }
        public int? Codigo { get; }
        public int? Subcodigo { get; }
        public int Estado { get; }

        public ErrorGraphMetaException(TipoErrorGraphMeta tipo, int estado, int? codigo, int? subcodigo, string mensaje, Exception interna = null)
            : base(mensaje, interna)
        {
            Tipo = tipo;
            Estado = estado;
            Codigo = codigo;
            Subcodigo = subcodigo;
        }

        public static ErrorGraphMetaException Desde(int estado, string cuerpo)
        {
            int? codigo = null;
            int? subcodigo = null;
            string mensaje = null;
            try
            {
                JToken error = string.IsNullOrWhiteSpace(cuerpo) ? null : JObject.Parse(cuerpo)["error"];
                if (error != null)
                {
                    codigo = (int?)error["code"];
                    subcodigo = (int?)error["error_subcode"];
                    mensaje = (string)error["message"];
                }
            }
            catch (Exception)
            {
                // No es JSON (una página de error): se queda en Otro.
            }
            return new ErrorGraphMetaException(Clasificar(codigo), estado, codigo, subcodigo,
                $"Meta ha contestado {estado}" + (codigo.HasValue ? $" (código {codigo}" + (subcodigo.HasValue ? $"/{subcodigo}" : "") + ")" : "")
                + (string.IsNullOrWhiteSpace(mensaje) ? "" : ": " + mensaje));
        }

        internal static TipoErrorGraphMeta Clasificar(int? codigo)
        {
            if (codigo == 190 || codigo == 102 || codigo == 463 || codigo == 467)
            {
                return TipoErrorGraphMeta.Token;
            }
            if (codigo == 10 || (codigo >= 200 && codigo <= 299))
            {
                return TipoErrorGraphMeta.Permiso;
            }
            if (codigo == 4 || codigo == 17 || codigo == 32 || codigo == 613 || codigo == 80001)
            {
                return TipoErrorGraphMeta.Limite;
            }
            return TipoErrorGraphMeta.Otro;
        }
    }

    /// <summary>
    /// NestoAPI#623 corte 5c: convierte los leads que da la API Graph (<c>/{form-id}/leads</c>) en <see cref="LeadMeta"/>. No tiene
    /// reglas propias: arma la misma tabla (cabecera + filas) que el fichero exportado y la lee <see cref="LectorLeadsMeta"/>, así los
    /// dos caminos reconocen los campos igual (preguntas del formulario, consentimiento, teléfono, código postal…).
    /// </summary>
    public static class LectorGraphLeadsMeta
    {
        /// <summary>Las casillas (custom_disclaimer_responses) que son el consentimiento de comunicaciones comerciales, por su clave.</summary>
        public static readonly string[] PALABRAS_CONSENTIMIENTO =
            { "recibir", "comunicacion", "novedad", "newsletter", "boletin", "promocion", "publicidad", "marketing", "comercial" };

        /// <summary>Cabecera con la que <see cref="LectorLeadsMeta"/> reconoce el consentimiento («deseo_recibir…»).</summary>
        internal const string CABECERA_CONSENTIMIENTO = "deseo_recibir_comunicaciones";

        /// <summary>
        /// Los campos estándar de los formularios de Meta → la cabecera con la que los lee <see cref="LectorLeadsMeta"/>. Las preguntas
        /// propias del formulario (situación profesional, qué quiere solicitar, el consentimiento si es pregunta) van con su nombre
        /// tal cual: el lector las reconoce por fragmento. null = se lee pero no se usa (la población sale del código postal).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> CAMPOS = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["full_name"] = "full_name",
            ["first_name"] = "first_name",
            ["last_name"] = "last_name",
            ["email"] = "email",
            ["work_email"] = "work_email",
            ["phone_number"] = "phone_number",
            ["work_phone_number"] = "work_phone_number",
            ["zip_code"] = "zip_code",
            ["post_code"] = "post_code",
            ["postal_code"] = "post_code",
            ["city"] = null,
            ["province"] = null,
            ["state"] = null,
            ["country"] = null,
            ["street_address"] = null,
            ["company_name"] = null,
        };

        public static List<LeadMeta> Leer(IEnumerable<JObject> leads)
        {
            List<Dictionary<string, string>> filas = (leads ?? Enumerable.Empty<JObject>()).Where(l => l != null).Select(Fila).ToList();
            var cabecera = new List<string> { "id", "created_time", "platform" };
            foreach (string columna in filas.SelectMany(f => f.Keys))
            {
                if (!cabecera.Contains(columna))
                {
                    cabecera.Add(columna);
                }
            }
            var tabla = new List<List<string>> { cabecera };
            tabla.AddRange(filas.Select(f => cabecera.Select(c => f.TryGetValue(c, out string v) ? v : null).ToList()));
            return LectorLeadsMeta.Leads(tabla);
        }

        private static Dictionary<string, string> Fila(JObject lead)
        {
            var fila = new Dictionary<string, string>
            {
                ["id"] = (string)lead["id"],
                ["created_time"] = TextoFecha(lead["created_time"]),
                ["platform"] = (string)lead["platform"]
            };
            if (lead["field_data"] is JArray campos)
            {
                foreach (JToken campo in campos)
                {
                    string nombre = (string)campo["name"];
                    if (string.IsNullOrWhiteSpace(nombre))
                    {
                        continue;
                    }
                    string valor = campo["values"] is JArray valores ? string.Join(", ", valores.Select(v => (string)v).Where(v => !string.IsNullOrWhiteSpace(v))) : null;
                    string cabecera;
                    if (CAMPOS.TryGetValue(nombre.Trim(), out string mapeada))
                    {
                        if (mapeada == null)
                        {
                            continue;
                        }
                        cabecera = mapeada;
                    }
                    else
                    {
                        cabecera = nombre.Trim();
                    }
                    if (!fila.ContainsKey(cabecera))
                    {
                        fila[cabecera] = valor;
                    }
                }
            }
            Completar(fila);
            if (lead["custom_disclaimer_responses"] is JArray casillas)
            {
                foreach (JToken casilla in casillas)
                {
                    string clave = (string)casilla["checkbox_key"];
                    if (!EsCasillaConsentimiento(clave))
                    {
                        continue;
                    }
                    bool marcada = EsMarcada(casilla["is_checked"]);
                    // Si hay varias casillas de comunicaciones, basta con una marcada.
                    if (!fila.TryGetValue(CABECERA_CONSENTIMIENTO, out string anterior) || anterior != "true")
                    {
                        fila[CABECERA_CONSENTIMIENTO] = marcada ? "true" : "false";
                    }
                }
            }
            return fila;
        }

        /// <summary>first_name + last_name → full_name; work_email y work_phone_number si no están los otros.</summary>
        private static void Completar(Dictionary<string, string> fila)
        {
            string Valor(string clave) => fila.TryGetValue(clave, out string v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
            if (Valor("full_name") == null)
            {
                string junto = string.Join(" ", new[] { Valor("first_name"), Valor("last_name") }.Where(v => v != null));
                if (!string.IsNullOrWhiteSpace(junto))
                {
                    fila["full_name"] = junto;
                }
            }
            if (Valor("email") == null && Valor("work_email") != null)
            {
                fila["email"] = Valor("work_email");
            }
            if (Valor("phone_number") == null && Valor("work_phone_number") != null)
            {
                fila["phone_number"] = Valor("work_phone_number");
            }
        }

        /// <summary>El lead trae la casilla de comunicaciones (en custom_disclaimer_responses o como pregunta del formulario).</summary>
        public static bool TraeConsentimiento(JObject lead)
        {
            if (lead?["custom_disclaimer_responses"] is JArray casillas && casillas.Any(c => EsCasillaConsentimiento((string)c["checkbox_key"])))
            {
                return true;
            }
            return lead?["field_data"] is JArray campos && campos.Any(c =>
            {
                string n = LectorLeadsMeta.NormalizarCabecera((string)c["name"]);
                return n.StartsWith("deseo_recibir") || n.Contains("recibir_por_correo");
            });
        }

        public static bool EsCasillaConsentimiento(string clave)
        {
            string normalizada = LectorLeadsMeta.NormalizarCabecera(clave);
            return !string.IsNullOrEmpty(normalizada) && PALABRAS_CONSENTIMIENTO.Any(p => normalizada.Contains(p));
        }

        private static bool EsMarcada(JToken valor)
        {
            if (valor == null || valor.Type == JTokenType.Null)
            {
                return false;
            }
            if (valor.Type == JTokenType.Boolean)
            {
                return (bool)valor;
            }
            string v = ((string)valor)?.Trim().ToLowerInvariant();
            return v == "1" || v == "true" || v == "yes" || v == "si" || v == "sí";
        }

        private static readonly Regex ZONA_SIN_DOS_PUNTOS = new Regex(@"([+-]\d{2})(\d{2})$", RegexOptions.Compiled);

        /// <summary>Graph da «2026-11-09T09:30:00+0000»: se pasa a «+00:00» para que lo entienda DateTimeOffset.</summary>
        internal static string NormalizarFecha(string fecha) =>
            string.IsNullOrWhiteSpace(fecha) ? fecha : ZONA_SIN_DOS_PUNTOS.Replace(fecha.Trim(), "$1:$2");

        /// <summary>
        /// La fecha como texto ISO con zona. Si quien parseó el JSON la convirtió ya en fecha (Json.NET lo hace por defecto, a la hora
        /// LOCAL del servidor), se vuelve a pasar a UTC.
        /// </summary>
        internal static string TextoFecha(JToken fecha)
        {
            if (fecha == null || fecha.Type == JTokenType.Null)
            {
                return null;
            }
            if (fecha.Type == JTokenType.Date)
            {
                object valor = ((JValue)fecha).Value;
                DateTime utc = valor is DateTimeOffset dto ? dto.UtcDateTime : ((DateTime)valor).ToUniversalTime();
                return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);
            }
            return NormalizarFecha((string)fecha);
        }

        public static DateTime? FechaUtc(JToken fecha) => FechaUtc(TextoFecha(fecha));

        /// <summary>El created_time de Graph en UTC (null si no se entiende).</summary>
        public static DateTime? FechaUtc(string fecha)
        {
            if (string.IsNullOrWhiteSpace(fecha))
            {
                return null;
            }
            return DateTimeOffset.TryParse(NormalizarFecha(fecha), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset f)
                ? f.UtcDateTime
                : (DateTime?)null;
        }
    }
}
