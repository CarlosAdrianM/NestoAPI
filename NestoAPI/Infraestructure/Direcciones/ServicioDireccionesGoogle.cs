using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Direcciones
{
    /// <summary>
    /// NestoAPI#306: proxy de Google Places para el autocompletado de direcciones en el alta de
    /// clientes. La key (GoogleMapsApiKey, la misma del geocoding) solo vive en el servidor y los
    /// 3 clientes (Nesto, NestoApp, TiendasNuevaVision) consumen los mismos endpoints. El
    /// sessionToken (un GUID que genera el cliente al empezar a teclear) agrupa autocomplete +
    /// detalle en una sesión de facturación de Google.
    /// </summary>
    public interface IServicioDireccionesGoogle
    {
        /// <param name="pais">Código ISO 3166-1 alpha-2 del país donde buscar (Nesto#436: el país
        /// de la dirección puede no ser España). Null o vacío = España.</param>
        Task<List<Models.Direcciones.SugerenciaDireccionDTO>> BuscarSugerencias(string texto, string sessionToken, string pais = null);
        Task<Models.Direcciones.DireccionDetalleDTO> LeerDetalle(string placeId, string sessionToken);
        /// <summary>
        /// NestoAPI#578: la dirección de unas coordenadas (geocodificación inversa de Google). Null si
        /// Google no encuentra nada.
        /// </summary>
        Task<Models.Direcciones.DireccionDetalleDTO> LeerDesdeCoordenadas(double latitud, double longitud);
        /// <summary>
        /// NestoAPI#874: geocodificación directa (calle + número) restringida al país y al código postal, para comprobar el
        /// número que se teclea cuando Places dio la calle sin él. Null si Google no encuentra nada.
        /// </summary>
        Task<Models.Direcciones.DireccionDetalleDTO> Geocodificar(string direccion, string codigoPostal, string pais);
    }

    public class ServicioDireccionesGoogle : IServicioDireccionesGoogle
    {
        public async Task<List<Models.Direcciones.SugerenciaDireccionDTO>> BuscarSugerencias(string texto, string sessionToken, string pais = null)
        {
            string url = "https://maps.googleapis.com/maps/api/place/autocomplete/json" +
                "?input=" + Uri.EscapeDataString(texto) +
                "&components=country:" + NormalizarPais(pais) + "&language=es&types=address" +
                (string.IsNullOrWhiteSpace(sessionToken) ? "" : "&sessiontoken=" + Uri.EscapeDataString(sessionToken)) +
                "&key=" + ConfigurationManager.AppSettings["GoogleMapsApiKey"];

            using (HttpClient client = new HttpClient())
            {
                string json = await client.GetStringAsync(url).ConfigureAwait(false);
                return ParsearSugerencias(json);
            }
        }

        public async Task<Models.Direcciones.DireccionDetalleDTO> LeerDetalle(string placeId, string sessionToken)
        {
            string url = "https://maps.googleapis.com/maps/api/place/details/json" +
                "?place_id=" + Uri.EscapeDataString(placeId) +
                "&fields=address_component,formatted_address&language=es" +
                (string.IsNullOrWhiteSpace(sessionToken) ? "" : "&sessiontoken=" + Uri.EscapeDataString(sessionToken)) +
                "&key=" + ConfigurationManager.AppSettings["GoogleMapsApiKey"];

            using (HttpClient client = new HttpClient())
            {
                string json = await client.GetStringAsync(url).ConfigureAwait(false);
                return ParsearDetalle(json);
            }
        }

        public async Task<Models.Direcciones.DireccionDetalleDTO> LeerDesdeCoordenadas(double latitud, double longitud)
        {
            var cultura = System.Globalization.CultureInfo.InvariantCulture;
            string url = "https://maps.googleapis.com/maps/api/geocode/json" +
                "?latlng=" + latitud.ToString(cultura) + "," + longitud.ToString(cultura) +
                "&language=es" +
                "&key=" + ConfigurationManager.AppSettings["GoogleMapsApiKey"];

            using (HttpClient client = new HttpClient())
            {
                string json = await client.GetStringAsync(url).ConfigureAwait(false);
                return ParsearGeocodificacionInversa(json);
            }
        }

        public async Task<Models.Direcciones.DireccionDetalleDTO> Geocodificar(string direccion, string codigoPostal, string pais)
        {
            string componentes = "country:" + NormalizarPais(pais) +
                (string.IsNullOrWhiteSpace(codigoPostal) ? "" : "|postal_code:" + codigoPostal.Trim());
            string url = "https://maps.googleapis.com/maps/api/geocode/json" +
                "?address=" + Uri.EscapeDataString(direccion ?? string.Empty) +
                "&components=" + Uri.EscapeDataString(componentes) +
                "&language=es" +
                "&key=" + ConfigurationManager.AppSettings["GoogleMapsApiKey"];

            using (HttpClient client = new HttpClient())
            {
                string json = await client.GetStringAsync(url).ConfigureAwait(false);
                return ParsearGeocodificacion(json);
            }
        }

        /// <summary>NestoAPI#874: el primer resultado de la geocodificación directa; ZERO_RESULTS → null (no es un error).</summary>
        internal static Models.Direcciones.DireccionDetalleDTO ParsearGeocodificacion(string json)
        {
            JObject respuesta = JObject.Parse(json);
            string status = (string)respuesta["status"];
            if (status == "ZERO_RESULTS")
            {
                return null;
            }
            if (status != "OK")
            {
                throw new Exception($"Google Geocoding devolvió {status}: {(string)respuesta["error_message"]}");
            }
            JToken primero = (respuesta["results"] ?? new JArray()).FirstOrDefault();
            return primero == null ? null : LeerComponentes(primero);
        }

        /// <summary>
        /// NestoAPI#578: de los resultados de la geocodificación inversa, el primero que es una dirección de
        /// calle (street_address/premise); si no hay, el primero con código postal; si tampoco, el primero.
        /// ZERO_RESULTS → null (no es un error).
        /// </summary>
        internal static Models.Direcciones.DireccionDetalleDTO ParsearGeocodificacionInversa(string json)
        {
            JObject respuesta = JObject.Parse(json);
            string status = (string)respuesta["status"];
            if (status == "ZERO_RESULTS")
            {
                return null;
            }
            if (status != "OK")
            {
                throw new Exception($"Google Geocoding devolvió {status}: {(string)respuesta["error_message"]}");
            }

            List<JToken> resultados = (respuesta["results"] ?? new JArray()).ToList();
            if (resultados.Count == 0)
            {
                return null;
            }
            bool EsDeTipo(JToken r, params string[] tipos) => (r["types"] ?? new JArray()).Any(t => tipos.Contains((string)t));
            bool TieneCodigoPostal(JToken r) => (r["address_components"] ?? new JArray()).Any(c => EsDeTipo(c, "postal_code"));

            JToken elegido = resultados.FirstOrDefault(r => EsDeTipo(r, "street_address", "premise"))
                ?? resultados.FirstOrDefault(TieneCodigoPostal)
                ?? resultados[0];
            return LeerComponentes(elegido);
        }

        /// <summary>
        /// Normaliza el país al formato de components de Places (ISO alpha-2 en minúsculas).
        /// Vacío = España (comportamiento de siempre); un valor con formato inválido lanza
        /// ArgumentException (el controller lo convierte en 400).
        /// </summary>
        internal static string NormalizarPais(string pais)
        {
            if (string.IsNullOrWhiteSpace(pais))
            {
                return "es";
            }
            string normalizado = pais.Trim().ToLowerInvariant();
            if (normalizado.Length != 2 || normalizado.Any(c => c < 'a' || c > 'z'))
            {
                throw new ArgumentException($"El país '{pais}' no es un código ISO de dos letras.");
            }
            return normalizado;
        }

        // Parsers puros (testeables sin HTTP). ZERO_RESULTS no es un error: lista vacía / null.
        internal static List<Models.Direcciones.SugerenciaDireccionDTO> ParsearSugerencias(string json)
        {
            JObject respuesta = JObject.Parse(json);
            string status = (string)respuesta["status"];
            if (status == "ZERO_RESULTS")
            {
                return new List<Models.Direcciones.SugerenciaDireccionDTO>();
            }
            if (status != "OK")
            {
                throw new Exception($"Google Places devolvió {status}: {(string)respuesta["error_message"]}");
            }
            return respuesta["predictions"]
                .Select(p => new Models.Direcciones.SugerenciaDireccionDTO
                {
                    Descripcion = (string)p["description"],
                    PlaceId = (string)p["place_id"]
                })
                .ToList();
        }

        internal static Models.Direcciones.DireccionDetalleDTO ParsearDetalle(string json)
        {
            JObject respuesta = JObject.Parse(json);
            string status = (string)respuesta["status"];
            if (status != "OK")
            {
                throw new Exception($"Google Places devolvió {status}: {(string)respuesta["error_message"]}");
            }

            return LeerComponentes(respuesta["result"]);
        }

        /// <summary>Los componentes de un resultado de Places o de Geocoding (tienen la misma forma).</summary>
        private static Models.Direcciones.DireccionDetalleDTO LeerComponentes(JToken resultado)
        {
            var detalle = new Models.Direcciones.DireccionDetalleDTO
            {
                DireccionFormateada = (string)resultado["formatted_address"]
            };

            foreach (JToken componente in resultado["address_components"] ?? Enumerable.Empty<JToken>())
            {
                var tipos = componente["types"].Select(t => (string)t).ToList();
                string valor = (string)componente["long_name"];
                if (tipos.Contains("country"))
                {
                    // Nesto#436: el país de la dirección, para que la ficha no asuma España.
                    detalle.Pais = valor;
                    detalle.PaisIso = ((string)componente["short_name"])?.ToUpperInvariant();
                }
                else if (tipos.Contains("route"))
                {
                    detalle.Calle = valor;
                }
                else if (tipos.Contains("street_number"))
                {
                    detalle.Numero = valor;
                }
                else if (tipos.Contains("postal_code"))
                {
                    detalle.CodigoPostal = valor;
                }
                else if (tipos.Contains("locality"))
                {
                    detalle.Poblacion = valor;
                }
                else if (tipos.Contains("administrative_area_level_2"))
                {
                    detalle.Provincia = ProvinciaSinPrefijo(valor);
                }
            }

            detalle.DireccionCalleNumero = DireccionCalleNumero(detalle.Calle, detalle.Numero, detalle.PaisIso);
            return detalle;
        }

        /// <summary>
        /// Sugerencia 1276 (Enrique, 16/11/26): Google da la provincia italiana como «Città Metropolitana di Roma Capitale» (37
        /// caracteres; la columna admite 30) y el alta del cliente fallaba. Se quita el tipo de entidad administrativa del
        /// principio (y «Capitale» de Roma): queda «Roma». Lo que no lleva prefijo conocido, tal cual.
        /// </summary>
        internal static string ProvinciaSinPrefijo(string provincia)
        {
            string texto = provincia?.Trim();
            if (string.IsNullOrEmpty(texto))
            {
                return texto;
            }
            foreach (string prefijo in PREFIJOS_PROVINCIA)
            {
                if (texto.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase) && texto.Length > prefijo.Length)
                {
                    texto = texto.Substring(prefijo.Length).Trim();
                    break;
                }
            }
            if (texto.EndsWith(" Capitale", StringComparison.OrdinalIgnoreCase))
            {
                texto = texto.Substring(0, texto.Length - " Capitale".Length).Trim();
            }
            return texto;
        }

        // Los más largos primero (para que «Provincia della» gane a «Provincia di»…)
        private static readonly string[] PREFIJOS_PROVINCIA =
        {
            "Libero consorzio comunale di ", "Città Metropolitana di ", "Citta Metropolitana di ", "Città metropolitana di ",
            "Provincia autonoma di ", "Provincia della ", "Provincia dell'", "Provincia del ", "Provincia di ",
            "Province of ", "County of "
        };

        /// <summary>NestoAPI#826: países donde el número de la calle va delante («12 Rue de la Paix»).</summary>
        internal static readonly HashSet<string> PAISES_NUMERO_DELANTE = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FR", "LU", "MC", "GB", "IE", "US", "CA", "AU", "NZ"
        };

        /// <summary>
        /// NestoAPI#826 (sugerencia 1058, Enrique): calle y número de Google en el formato del país.
        /// España (o sin país): «Calle, Número», el formato de la BD. Francia y <see cref="PAISES_NUMERO_DELANTE"/>:
        /// «Número Calle». El resto: «Calle Número».
        /// </summary>
        internal static string DireccionCalleNumero(string calle, string numero, string paisIso)
        {
            calle = calle?.Trim();
            numero = numero?.Trim();
            if (string.IsNullOrWhiteSpace(numero))
            {
                return calle;
            }
            if (string.IsNullOrWhiteSpace(calle))
            {
                return numero;
            }
            if (!GestorClientes.EsPaisExtranjero(paisIso))
            {
                return $"{calle}, {numero}";
            }
            return PAISES_NUMERO_DELANTE.Contains(paisIso.Trim())
                ? $"{numero} {calle}"
                : $"{calle} {numero}";
        }
    }
}
