using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NestoAPI.Models.Agencias;
using Newtonsoft.Json.Linq;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#767: el recorrido calculado. <see cref="Orden"/> son índices de las paradas que se pasaron, en el orden de visita.</summary>
    public class RecorridoCalculado
    {
        public List<int> Orden { get; set; } = new List<int>();
        public decimal? Kilometros { get; set; }
        public int? Minutos { get; set; }
        /// <summary><see cref="OrdenSugeridoRepartoDTO.MOTOR_OSRM"/> o <see cref="OrdenSugeridoRepartoDTO.MOTOR_HEURISTICA"/>.</summary>
        public string Motor { get; set; }
    }

    /// <summary>
    /// NestoAPI#767: un motor de rutas. Recorrido ABIERTO con los dos extremos fijos: sale de <c>origen</c> (la posición de la PDA,
    /// o Algete) y termina en <c>destino</c> (el almacén de Algete por defecto, o el punto final que elija el repartidor, p. ej. su casa). Así, si recalcula a media ruta,
    /// no deja para el final la parada más lejana de Algete.
    /// </summary>
    public interface IMotorRutas
    {
        /// <summary>Null si no puede (no responde, error, demasiadas paradas): entonces se usa la heurística.</summary>
        Task<RecorridoCalculado> Calcular(CoordenadasGeo origen, CoordenadasGeo destino, IReadOnlyList<CoordenadasGeo> paradas);
    }

    /// <summary>
    /// NestoAPI#767: OSRM <c>/trip</c> del servidor público (router.project-osrm.org, perfil coche) con
    /// <c>roundtrip=false&amp;source=first&amp;destination=last</c> (origen primero, destino último). Con un tope de
    /// <see cref="TIMEOUT"/>: la PDA está esperando. El servidor de demostración no admite más de 100 puntos por petición.
    /// </summary>
    public class MotorRutasOsrm : IMotorRutas
    {
        internal const string URL = "https://router.project-osrm.org/trip/v1/driving/";
        internal const int MAXIMO_PUNTOS = 100;
        public static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(5);

        private static readonly HttpClient cliente = CrearCliente();

        private static HttpClient CrearCliente()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(GeocodificadorNominatim.USER_AGENT);
            return http;
        }

        internal static string Url(CoordenadasGeo origen, CoordenadasGeo destino, IReadOnlyList<CoordenadasGeo> paradas)
        {
            IEnumerable<CoordenadasGeo> puntos = new[] { origen }.Concat(paradas).Concat(new[] { destino });
            string coordenadas = string.Join(";", puntos.Select(p =>
                p.Longitud.ToString("0.######", CultureInfo.InvariantCulture) + "," + p.Latitud.ToString("0.######", CultureInfo.InvariantCulture)));
            return URL + coordenadas + "?roundtrip=false&source=first&destination=last&overview=false&steps=false";
        }

        public async Task<RecorridoCalculado> Calcular(CoordenadasGeo origen, CoordenadasGeo destino, IReadOnlyList<CoordenadasGeo> paradas)
        {
            if (paradas == null || paradas.Count == 0 || paradas.Count + 2 > MAXIMO_PUNTOS)
            {
                return null;
            }
            try
            {
                using (var tope = new CancellationTokenSource(TIMEOUT))
                using (HttpResponseMessage respuesta = await cliente.GetAsync(Url(origen, destino, paradas), tope.Token).ConfigureAwait(false))
                {
                    string json = await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return respuesta.IsSuccessStatusCode ? Leer(json, paradas.Count) : null;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException
                || ex is Newtonsoft.Json.JsonException || ex is InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// La respuesta de /trip: <c>waypoints[i].waypoint_index</c> es la posición en el viaje del punto i de la petición (el 0 es el
        /// origen y el último, Algete). Null si no es «Ok» o no cuadra.
        /// </summary>
        internal static RecorridoCalculado Leer(string json, int paradas)
        {
            JObject raiz = JObject.Parse(json);
            if (!string.Equals((string)raiz["code"], "Ok", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            var waypoints = raiz["waypoints"] as JArray;
            var trips = raiz["trips"] as JArray;
            if (waypoints == null || waypoints.Count != paradas + 2 || trips == null || trips.Count != 1)
            {
                return null;
            }
            var posiciones = new List<(int Parada, int Posicion)>();
            for (int i = 1; i <= paradas; i++)
            {
                int? posicion = (int?)waypoints[i]["waypoint_index"];
                int? viaje = (int?)waypoints[i]["trips_index"] ?? 0;
                if (!posicion.HasValue || viaje != 0)
                {
                    return null;
                }
                posiciones.Add((i - 1, posicion.Value));
            }
            if (posiciones.Select(p => p.Posicion).Distinct().Count() != paradas)
            {
                return null;
            }
            double? metros = (double?)trips[0]["distance"];
            double? segundos = (double?)trips[0]["duration"];
            return new RecorridoCalculado
            {
                Orden = posiciones.OrderBy(p => p.Posicion).Select(p => p.Parada).ToList(),
                Kilometros = metros.HasValue ? Math.Round((decimal)metros.Value / 1000m, 1) : (decimal?)null,
                Minutos = segundos.HasValue ? (int)Math.Round(segundos.Value / 60d) : (int?)null,
                Motor = OrdenSugeridoRepartoDTO.MOTOR_OSRM
            };
        }
    }

    /// <summary>
    /// NestoAPI#767: el respaldo local cuando OSRM falla o tarda: vecino más próximo desde el origen y luego 2-opt (con los dos
    /// extremos fijos) sobre distancias haversine. Determinista (en empate, la parada de índice menor). Los kilómetros son una
    /// estimación: línea recta × <see cref="FACTOR_CARRETERA"/>; sin minutos.
    /// </summary>
    public static class HeuristicaRutas
    {
        public const double FACTOR_CARRETERA = 1.3;
        private const double RADIO_TIERRA_KM = 6371.0088;
        private const int MAXIMO_PASADAS_2OPT = 200;
        private const double EPSILON = 1e-9;

        public static double DistanciaKm(CoordenadasGeo a, CoordenadasGeo b)
        {
            double lat1 = Radianes((double)a.Latitud), lat2 = Radianes((double)b.Latitud);
            double dLat = lat2 - lat1, dLon = Radianes((double)b.Longitud - (double)a.Longitud);
            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * RADIO_TIERRA_KM * Math.Asin(Math.Min(1, Math.Sqrt(h)));
        }

        private static double Radianes(double grados) => grados * Math.PI / 180d;

        public static RecorridoCalculado Calcular(CoordenadasGeo origen, CoordenadasGeo destino, IReadOnlyList<CoordenadasGeo> paradas)
        {
            int n = paradas?.Count ?? 0;
            if (n == 0)
            {
                return new RecorridoCalculado { Motor = OrdenSugeridoRepartoDTO.MOTOR_HEURISTICA, Kilometros = Kilometros(DistanciaKm(origen, destino)) };
            }
            // Puntos: 0 = origen, 1..n = paradas, n+1 = destino.
            var puntos = new List<CoordenadasGeo> { origen };
            puntos.AddRange(paradas);
            puntos.Add(destino);
            int total = puntos.Count;
            var d = new double[total, total];
            for (int i = 0; i < total; i++)
            {
                for (int j = 0; j < total; j++)
                {
                    d[i, j] = i == j ? 0 : DistanciaKm(puntos[i], puntos[j]);
                }
            }

            // Vecino más próximo.
            var camino = new List<int> { 0 };
            var quedan = new SortedSet<int>(Enumerable.Range(1, n));
            int actual = 0;
            while (quedan.Count > 0)
            {
                int mejor = -1;
                double mejorDistancia = double.MaxValue;
                foreach (int candidata in quedan)
                {
                    if (d[actual, candidata] < mejorDistancia - EPSILON)
                    {
                        mejor = candidata;
                        mejorDistancia = d[actual, candidata];
                    }
                }
                camino.Add(mejor);
                _ = quedan.Remove(mejor);
                actual = mejor;
            }
            camino.Add(n + 1);

            // 2-opt con los extremos fijos: invertir el tramo [i..k] si acorta.
            bool mejora = true;
            for (int pasada = 0; mejora && pasada < MAXIMO_PASADAS_2OPT; pasada++)
            {
                mejora = false;
                for (int i = 1; i < camino.Count - 2; i++)
                {
                    for (int k = i + 1; k < camino.Count - 1; k++)
                    {
                        double antes = d[camino[i - 1], camino[i]] + d[camino[k], camino[k + 1]];
                        double despues = d[camino[i - 1], camino[k]] + d[camino[i], camino[k + 1]];
                        if (despues < antes - EPSILON)
                        {
                            camino.Reverse(i, k - i + 1);
                            mejora = true;
                        }
                    }
                }
            }

            double km = 0;
            for (int i = 0; i < camino.Count - 1; i++)
            {
                km += d[camino[i], camino[i + 1]];
            }
            return new RecorridoCalculado
            {
                Orden = camino.Skip(1).Take(n).Select(p => p - 1).ToList(),
                Kilometros = Kilometros(km),
                Minutos = null,
                Motor = OrdenSugeridoRepartoDTO.MOTOR_HEURISTICA
            };
        }

        private static decimal Kilometros(double lineaRecta) => Math.Round((decimal)(lineaRecta * FACTOR_CARRETERA), 1);
    }

    /// <summary>NestoAPI#767: el recorrido sugerido: OSRM y, si no responde o falla, la heurística local.</summary>
    public interface IPlanificadorRecorrido
    {
        Task<RecorridoCalculado> Calcular(CoordenadasGeo origen, CoordenadasGeo destino, IReadOnlyList<CoordenadasGeo> paradas);
    }

    public class PlanificadorRecorrido : IPlanificadorRecorrido
    {
        private readonly IMotorRutas motor;

        /// <param name="motor">Null: solo la heurística.</param>
        public PlanificadorRecorrido(IMotorRutas motor)
        {
            this.motor = motor;
        }

        public async Task<RecorridoCalculado> Calcular(CoordenadasGeo origen, CoordenadasGeo destino, IReadOnlyList<CoordenadasGeo> paradas)
        {
            int n = paradas?.Count ?? 0;
            if (n >= 1 && motor != null)
            {
                RecorridoCalculado calculado = null;
                try
                {
                    calculado = await motor.Calcular(origen, destino, paradas).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Reparto propio #767] El motor de rutas ha fallado; se usa la heurística local. {ex.Message}", ex));
                }
                if (calculado != null && calculado.Orden != null && calculado.Orden.Count == n
                    && calculado.Orden.OrderBy(i => i).SequenceEqual(Enumerable.Range(0, n)))
                {
                    return calculado;
                }
            }
            return HeuristicaRutas.Calcular(origen, destino, paradas ?? new List<CoordenadasGeo>());
        }
    }
}
