using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#770: de coordenadas a una dirección legible para el sello de la firma.</summary>
    public interface IGeocodificadorInverso
    {
        /// <summary>«Calle Mayor 1, 28110 Algete», o null si no se sabe. Puede lanzar (quien lo llama sigue sin dirección).</summary>
        Task<string> Direccion(decimal latitud, decimal longitud);
    }

    /// <summary>
    /// NestoAPI#770: /reverse de Nominatim, DENTRO de la entrega (decisión de Carlos 31/10/26), por eso con un tope corto
    /// (<see cref="TIEMPO_MAXIMO"/>, contando la espera del turno, que es el mismo que el de los jobs de #767) y con caché en
    /// memoria por coordenadas redondeadas a 4 decimales (~10 m). Si no llega a tiempo, la firma se sella solo con coordenadas.
    /// </summary>
    public class GeocodificadorInversoNominatim : IGeocodificadorInverso
    {
        internal const string URL = "https://nominatim.openstreetmap.org/reverse";
        public static readonly TimeSpan TIEMPO_MAXIMO = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan DURACION_CACHE = TimeSpan.FromDays(30);
        private static readonly MemoryCache cache = new MemoryCache("GeocodificacionInversaFirmas");

        internal static string Url(decimal latitud, decimal longitud)
            => URL + "?format=jsonv2&zoom=18&addressdetails=1&lat=" + latitud.ToString("0.######", CultureInfo.InvariantCulture)
                + "&lon=" + longitud.ToString("0.######", CultureInfo.InvariantCulture);

        internal static string ClaveCache(decimal latitud, decimal longitud)
            => Math.Round(latitud, 4).ToString("0.0000", CultureInfo.InvariantCulture) + "," + Math.Round(longitud, 4).ToString("0.0000", CultureInfo.InvariantCulture);

        public async Task<string> Direccion(decimal latitud, decimal longitud)
        {
            string clave = ClaveCache(latitud, longitud);
            if (cache.Get(clave) is string guardada)
            {
                return guardada;
            }
            using (var cancelar = new CancellationTokenSource(TIEMPO_MAXIMO))
            {
                string json = await GeocodificadorNominatim.PedirConTurno(Url(latitud, longitud), cancelar.Token).ConfigureAwait(false);
                string direccion = Leer(json);
                if (direccion != null)
                {
                    _ = cache.Add(clave, direccion, DateTimeOffset.UtcNow.Add(DURACION_CACHE));
                }
                return direccion;
            }
        }

        /// <summary>
        /// «Calle Mayor 1, 28110 Algete» a partir de address (road, house_number, postcode, city/town/village…); sin calle ni
        /// población, el display_name; null si no hay nada (o el JSON trae error).
        /// </summary>
        internal static string Leer(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            JObject raiz = JObject.Parse(json);
            if (raiz["error"] != null)
            {
                return null;
            }
            if (raiz["address"] is JObject direccion)
            {
                string calle = Primero(direccion, "road", "pedestrian", "footway", "path", "square", "neighbourhood");
                string numero = Primero(direccion, "house_number");
                string cp = Primero(direccion, "postcode");
                string poblacion = Primero(direccion, "city", "town", "village", "municipality", "hamlet", "suburb");
                var partes = new List<string>();
                if (calle != null)
                {
                    partes.Add(numero == null ? calle : calle + " " + numero);
                }
                string lugar = string.Join(" ", new[] { cp, poblacion }.Where(p => p != null));
                if (lugar.Length > 0)
                {
                    partes.Add(lugar);
                }
                if (calle != null || poblacion != null)
                {
                    return string.Join(", ", partes);
                }
            }
            string completo = (string)raiz["display_name"];
            return string.IsNullOrWhiteSpace(completo) ? null : completo.Trim();
        }

        private static string Primero(JObject objeto, params string[] campos)
        {
            foreach (string campo in campos)
            {
                string valor = (string)objeto[campo];
                if (!string.IsNullOrWhiteSpace(valor))
                {
                    return valor.Trim();
                }
            }
            return null;
        }
    }
}
