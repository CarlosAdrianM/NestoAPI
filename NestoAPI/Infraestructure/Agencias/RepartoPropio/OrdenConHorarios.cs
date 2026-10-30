using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models.Agencias;
using Newtonsoft.Json.Linq;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#769: minutos (y, si se saben, kilómetros) de cada punto a cada punto. Los puntos son 0 = origen, 1..n = paradas
    /// y n+1 = destino.
    /// </summary>
    public class MatrizTiempos
    {
        public MatrizTiempos(double[,] minutos, double[,] kilometros, string motor)
        {
            Minutos = minutos ?? throw new ArgumentNullException(nameof(minutos));
            Kilometros = kilometros;
            Motor = motor;
        }

        public double[,] Minutos { get; }
        /// <summary>Null si el motor no los da (entonces se estiman con la línea recta).</summary>
        public double[,] Kilometros { get; }
        /// <summary><see cref="OrdenSugeridoRepartoDTO.MOTOR_OSRM"/> o <see cref="OrdenSugeridoRepartoDTO.MOTOR_HEURISTICA"/>.</summary>
        public string Motor { get; }
        public int Puntos => Minutos.GetLength(0);

        /// <summary>
        /// El respaldo: línea recta × <see cref="HeuristicaRutas.FACTOR_CARRETERA"/> a <see cref="ServicioRepartoPropio.VELOCIDAD_MEDIA_KMH"/>
        /// (lo mismo que usa <see cref="ServicioRepartoPropio.EstimarLlegadas"/> cuando el motor no da los tramos).
        /// </summary>
        public static MatrizTiempos Heuristica(IReadOnlyList<CoordenadasGeo> puntos)
        {
            int total = puntos.Count;
            var minutos = new double[total, total];
            var kilometros = new double[total, total];
            for (int i = 0; i < total; i++)
            {
                for (int j = 0; j < total; j++)
                {
                    double km = i == j ? 0 : HeuristicaRutas.DistanciaKm(puntos[i], puntos[j]) * HeuristicaRutas.FACTOR_CARRETERA;
                    kilometros[i, j] = km;
                    minutos[i, j] = km / ServicioRepartoPropio.VELOCIDAD_MEDIA_KMH * 60d;
                }
            }
            return new MatrizTiempos(minutos, kilometros, OrdenSugeridoRepartoDTO.MOTOR_HEURISTICA);
        }
    }

    /// <summary>NestoAPI#769: la matriz de tiempos por carretera.</summary>
    public interface IMatrizTiempos
    {
        /// <summary>Null si no puede (no responde, error, demasiados puntos): entonces se usa <see cref="MatrizTiempos.Heuristica"/>.</summary>
        Task<MatrizTiempos> Calcular(IReadOnlyList<CoordenadasGeo> puntos);
    }

    /// <summary>
    /// NestoAPI#769: OSRM <c>/table</c> (el mismo servidor, User-Agent y tope de <see cref="MotorRutasOsrm.TIMEOUT"/> que <c>/trip</c>),
    /// una sola petición con todos los puntos y como mucho <see cref="MAXIMO_PUNTOS"/>.
    /// </summary>
    public class MatrizTiemposOsrm : IMatrizTiempos
    {
        internal const string URL = "https://router.project-osrm.org/table/v1/driving/";
        public const int MAXIMO_PUNTOS = 60;

        internal static string Url(IReadOnlyList<CoordenadasGeo> puntos)
        {
            string coordenadas = string.Join(";", puntos.Select(p =>
                p.Longitud.ToString("0.######", CultureInfo.InvariantCulture) + "," + p.Latitud.ToString("0.######", CultureInfo.InvariantCulture)));
            return URL + coordenadas + "?annotations=duration,distance";
        }

        public async Task<MatrizTiempos> Calcular(IReadOnlyList<CoordenadasGeo> puntos)
        {
            if (puntos == null || puntos.Count < 2 || puntos.Count > MAXIMO_PUNTOS)
            {
                return null;
            }
            try
            {
                using (var tope = new CancellationTokenSource(MotorRutasOsrm.TIMEOUT))
                using (HttpResponseMessage respuesta = await MotorRutasOsrm.Http.GetAsync(Url(puntos), tope.Token).ConfigureAwait(false))
                {
                    string json = await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return respuesta.IsSuccessStatusCode ? Leer(json, puntos.Count) : null;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException
                || ex is Newtonsoft.Json.JsonException || ex is InvalidOperationException || ex is InvalidCastException
                || ex is FormatException || ex is OverflowException)
            {
                return null;
            }
        }

        /// <summary>
        /// La respuesta de /table: <c>durations[i][j]</c> en segundos y <c>distances[i][j]</c> en metros. Null si no es «Ok», no
        /// cuadra o falta algún tramo (sin ruta); sin distancias válidas, solo minutos.
        /// </summary>
        internal static MatrizTiempos Leer(string json, int puntos)
        {
            JObject raiz = JObject.Parse(json);
            if (!string.Equals((string)raiz["code"], "Ok", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            double[,] segundos = Tabla(raiz["durations"] as JArray, puntos);
            if (segundos == null)
            {
                return null;
            }
            double[,] metros = Tabla(raiz["distances"] as JArray, puntos);
            var minutos = new double[puntos, puntos];
            double[,] kilometros = metros == null ? null : new double[puntos, puntos];
            for (int i = 0; i < puntos; i++)
            {
                for (int j = 0; j < puntos; j++)
                {
                    minutos[i, j] = segundos[i, j] / 60d;
                    if (kilometros != null)
                    {
                        kilometros[i, j] = metros[i, j] / 1000d;
                    }
                }
            }
            return new MatrizTiempos(minutos, kilometros, OrdenSugeridoRepartoDTO.MOTOR_OSRM);
        }

        private static double[,] Tabla(JArray filas, int puntos)
        {
            if (filas == null || filas.Count != puntos)
            {
                return null;
            }
            var tabla = new double[puntos, puntos];
            for (int i = 0; i < puntos; i++)
            {
                if (!(filas[i] is JArray fila) || fila.Count != puntos)
                {
                    return null;
                }
                for (int j = 0; j < puntos; j++)
                {
                    if (fila[j] == null || (fila[j].Type != JTokenType.Float && fila[j].Type != JTokenType.Integer))
                    {
                        return null;
                    }
                    tabla[i, j] = Math.Max(0, (double)fila[j]);
                }
            }
            return tabla;
        }
    }

    /// <summary>NestoAPI#769: cómo se llega a una parada con su horario preferible.</summary>
    public struct LlegadaParada
    {
        /// <summary>La hora a la que se entrega (si se espera a que abra, la de apertura). Sin redondear.</summary>
        public DateTime Hora { get; set; }
        /// <summary>Tiene horario preferible (algún día).</summary>
        public bool ConHorario { get; set; }
        public bool Fuera { get; set; }
        public double MinutosEspera { get; set; }
    }

    /// <summary>NestoAPI#769: la regla única de «llega dentro o fuera de su horario», la de #763 y la de la segunda pasada.</summary>
    public static class VentanaHorario
    {
        /// <summary>La hora en minutos enteros (como se enseña <see cref="EnvioRepartoDTO.LlegadaEstimada"/>).</summary>
        public static DateTime Minuto(DateTime hora) => new DateTime(hora.Year, hora.Month, hora.Day, hora.Hour, hora.Minute, 0);

        /// <summary>
        /// Llega a <paramref name="reloj"/>: sin horario, nunca está fuera; con horario, dentro si alguna franja de ese día contiene
        /// la hora; si llega antes de que abra una franja de ese día y la espera no pasa de <paramref name="esperaTopeMinutos"/>,
        /// espera y entrega al abrir. Un día sin franjas (tiene horario, pero no ese día) es fuera siempre.
        /// </summary>
        public static LlegadaParada Llegar(DateTime reloj, IReadOnlyList<FranjaHorario> franjas, double esperaTopeMinutos)
        {
            var llegada = new LlegadaParada { Hora = reloj };
            if (franjas == null || franjas.Count == 0)
            {
                return llegada;
            }
            llegada.ConHorario = true;
            int dia = FormatoHorarioPreferible.DiaSemana(reloj.DayOfWeek);
            TimeSpan hora = Minuto(reloj).TimeOfDay;
            // Sin LINQ: la segunda pasada llama aquí miles de veces.
            FranjaHorario siguiente = null;
            for (int i = 0; i < franjas.Count; i++)
            {
                FranjaHorario franja = franjas[i];
                if (franja.DiaSemana != dia)
                {
                    continue;
                }
                if (franja.Contiene(hora))
                {
                    return llegada;
                }
                if (franja.Desde > hora && (siguiente == null || franja.Desde < siguiente.Desde))
                {
                    siguiente = franja;
                }
            }
            if (siguiente != null && esperaTopeMinutos > 0)
            {
                DateTime abre = reloj.Date + siguiente.Desde;
                double espera = (abre - reloj).TotalMinutes;
                if (espera <= esperaTopeMinutos)
                {
                    llegada.Hora = abre;
                    llegada.MinutosEspera = espera;
                    return llegada;
                }
            }
            llegada.Fuera = true;
            return llegada;
        }
    }

    /// <summary>NestoAPI#769: un orden evaluado con la matriz de tiempos y los horarios.</summary>
    public class EvaluacionOrden
    {
        /// <summary>Índices de las paradas (0..n-1) en el orden de visita.</summary>
        public List<int> Orden { get; set; } = new List<int>();
        /// <summary>A qué hora se entrega cada parada (en el orden de visita), en minutos enteros.</summary>
        public List<DateTime> Llegadas { get; set; } = new List<DateTime>();
        public List<bool> FueraPorParada { get; set; } = new List<bool>();
        public int Fuera { get; set; }
        /// <summary>Del origen al destino: conducción + <see cref="ServicioRepartoPropio.MINUTOS_POR_PARADA"/> en cada parada + esperas.</summary>
        public double MinutosTotales { get; set; }
        public double MinutosConduccion { get; set; }
        /// <summary>Lo que minimiza la búsqueda: minutos totales + <see cref="OrdenConHorarios.PENALIZACION_FUERA_MINUTOS"/> por parada fuera.</summary>
        public double Coste => MinutosTotales + OrdenConHorarios.PENALIZACION_FUERA_MINUTOS * Fuera;
    }

    /// <summary>
    /// NestoAPI#769: la segunda pasada del orden sugerido, con ventanas horarias. Determinista (mismos datos, mismo orden; en
    /// empate gana lo primero que se probó). Parte del recorrido más corto (#767) y de una inserción más barata en ese mismo orden,
    /// mejora los dos con 2-opt y or-opt (mover tramos de 1 a 3 paradas) y se queda con el que deja menos paradas fuera y, a
    /// igualdad, el de menor coste. Quien la usa decide si el resultado compensa (solo si hay menos fuera que con el más corto).
    /// </summary>
    public static class OrdenConHorarios
    {
        /// <summary>Lo más que se espera en la puerta a que abra la franja.</summary>
        public const int ESPERA_TOPE_MINUTOS = 30;
        /// <summary>Lo que «cuesta» que una parada llegue fuera de su horario, en minutos equivalentes.</summary>
        public const int PENALIZACION_FUERA_MINUTOS = 45;
        private const int MAXIMO_PASADAS = 10; // Tope fijo (no de tiempo) para que sea determinista.
        private const int MAXIMO_TRAMO_OR_OPT = 3;
        private const double EPSILON = 1e-6;

        /// <param name="franjas">Las de cada parada (índice 0..n-1); null o vacía si no tiene horario.</param>
        /// <param name="orden">Paradas a visitar, en orden (puede ser solo una parte, para la inserción).</param>
        public static EvaluacionOrden Evaluar(MatrizTiempos matriz, IReadOnlyList<List<FranjaHorario>> franjas, DateTime salida, IReadOnlyList<int> orden)
            => Evaluar(matriz, franjas, salida, orden, detalle: true);

        /// <summary>Sin <paramref name="detalle"/>, solo los totales (para la búsqueda: sin copiar el orden ni guardar las llegadas).</summary>
        private static EvaluacionOrden Evaluar(MatrizTiempos matriz, IReadOnlyList<List<FranjaHorario>> franjas, DateTime salida, IReadOnlyList<int> orden, bool detalle)
        {
            int destino = franjas.Count + 1;
            var evaluacion = new EvaluacionOrden { Orden = detalle || !(orden is List<int> lista) ? orden.ToList() : lista };
            DateTime reloj = salida;
            double conduccion = 0;
            int anterior = 0;
            for (int i = 0; i < orden.Count; i++)
            {
                int parada = orden[i];
                double tramo = matriz.Minutos[anterior, parada + 1];
                conduccion += tramo;
                LlegadaParada llegada = VentanaHorario.Llegar(reloj.AddMinutes(tramo), franjas[parada], ESPERA_TOPE_MINUTOS);
                if (detalle)
                {
                    evaluacion.Llegadas.Add(VentanaHorario.Minuto(llegada.Hora));
                    evaluacion.FueraPorParada.Add(llegada.Fuera);
                }
                if (llegada.Fuera)
                {
                    evaluacion.Fuera++;
                }
                reloj = llegada.Hora.AddMinutes(ServicioRepartoPropio.MINUTOS_POR_PARADA);
                anterior = parada + 1;
            }
            double vuelta = matriz.Minutos[anterior, destino];
            conduccion += vuelta;
            reloj = reloj.AddMinutes(vuelta);
            evaluacion.MinutosConduccion = conduccion;
            evaluacion.MinutosTotales = (reloj - salida).TotalMinutes;
            return evaluacion;
        }

        /// <summary>El mejor orden que encuentra (puede ser <paramref name="ordenInicial"/>): menos paradas fuera y, a igualdad, menor coste.</summary>
        public static EvaluacionOrden Mejorar(MatrizTiempos matriz, IReadOnlyList<List<FranjaHorario>> franjas, DateTime salida, IReadOnlyList<int> ordenInicial)
        {
            EvaluacionOrden inicial = Evaluar(matriz, franjas, salida, ordenInicial);
            var candidatos = new List<EvaluacionOrden>
            {
                inicial,
                Pulir(matriz, franjas, salida, inicial),
                Pulir(matriz, franjas, salida, Insercion(matriz, franjas, salida, ordenInicial))
            };
            EvaluacionOrden mejor = candidatos[0];
            foreach (EvaluacionOrden candidato in candidatos.Skip(1))
            {
                if (candidato.Fuera < mejor.Fuera || (candidato.Fuera == mejor.Fuera && candidato.Coste < mejor.Coste - EPSILON))
                {
                    mejor = candidato;
                }
            }
            return Evaluar(matriz, franjas, salida, mejor.Orden);
        }

        /// <summary>Inserción más barata: las paradas en el orden de entrada, cada una en el hueco que deja el menor coste.</summary>
        private static EvaluacionOrden Insercion(MatrizTiempos matriz, IReadOnlyList<List<FranjaHorario>> franjas, DateTime salida, IReadOnlyList<int> ordenInicial)
        {
            var camino = new List<int>();
            foreach (int parada in ordenInicial)
            {
                int mejorHueco = 0;
                double mejorCoste = double.MaxValue;
                for (int hueco = 0; hueco <= camino.Count; hueco++)
                {
                    camino.Insert(hueco, parada);
                    double coste = Evaluar(matriz, franjas, salida, camino, detalle: false).Coste;
                    camino.RemoveAt(hueco);
                    if (coste < mejorCoste - EPSILON)
                    {
                        mejorCoste = coste;
                        mejorHueco = hueco;
                    }
                }
                camino.Insert(mejorHueco, parada);
            }
            return Evaluar(matriz, franjas, salida, camino);
        }

        /// <summary>Mejora local por coste: 2-opt (invertir un tramo) y or-opt (mover un tramo de 1 a 3 paradas a otro hueco).</summary>
        private static EvaluacionOrden Pulir(MatrizTiempos matriz, IReadOnlyList<List<FranjaHorario>> franjas, DateTime salida, EvaluacionOrden desde)
        {
            EvaluacionOrden actual = desde;
            int n = actual.Orden.Count;
            bool mejora = true;
            for (int pasada = 0; mejora && pasada < MAXIMO_PASADAS; pasada++)
            {
                mejora = false;
                for (int i = 0; i < n - 1; i++)
                {
                    for (int k = i + 1; k < n; k++)
                    {
                        var candidato = new List<int>(actual.Orden);
                        candidato.Reverse(i, k - i + 1);
                        if (Aceptar(matriz, franjas, salida, candidato, ref actual))
                        {
                            mejora = true;
                        }
                    }
                }
                for (int largo = 1; largo <= MAXIMO_TRAMO_OR_OPT && largo < n; largo++)
                {
                    for (int i = 0; i + largo <= n; i++)
                    {
                        for (int hueco = 0; hueco <= n - largo; hueco++)
                        {
                            if (hueco == i)
                            {
                                continue;
                            }
                            var resto = new List<int>(actual.Orden);
                            List<int> tramo = resto.GetRange(i, largo);
                            resto.RemoveRange(i, largo);
                            resto.InsertRange(hueco, tramo);
                            if (Aceptar(matriz, franjas, salida, resto, ref actual))
                            {
                                mejora = true;
                            }
                        }
                    }
                }
            }
            return actual;
        }

        private static bool Aceptar(MatrizTiempos matriz, IReadOnlyList<List<FranjaHorario>> franjas, DateTime salida, List<int> candidato, ref EvaluacionOrden actual)
        {
            EvaluacionOrden evaluado = Evaluar(matriz, franjas, salida, candidato, detalle: false);
            if (evaluado.Coste < actual.Coste - EPSILON)
            {
                actual = evaluado;
                return true;
            }
            return false;
        }
    }
}
