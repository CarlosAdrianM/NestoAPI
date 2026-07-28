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

            JToken resultado = respuesta["result"];
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
                    detalle.Provincia = valor;
                }
            }

            return detalle;
        }
    }
}
