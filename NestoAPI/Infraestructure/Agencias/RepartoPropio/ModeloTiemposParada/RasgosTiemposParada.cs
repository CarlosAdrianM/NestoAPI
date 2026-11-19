using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.ML.Data;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio.ModeloTiemposParada
{
    /// <summary>
    /// NestoAPI#881 (corte 3): lo que el modelo sabe de una parada ANTES de hacerla (nada de lo que pasó en ella: sin fuga). Los numéricos
    /// que faltan van como NaN (LightGBM los trata como «sin dato»).
    /// </summary>
    public class RasgosTiempoParada
    {
        public string TipoParada { get; set; }
        /// <summary>El nombre del tipo de servicio técnico, o «(ninguno)».</summary>
        public string ServicioTecnico { get; set; }
        public float Bultos { get; set; }
        /// <summary>1 si lleva cobro (reembolso o nota de cobro), 0 si no.</summary>
        public float LlevaCobro { get; set; }
        /// <summary>Efectivo, Enlace, Tarjeta, NoCobrado… o «(ninguno)».</summary>
        public string FormaCobro { get; set; }
        /// <summary>Las 3 primeras cifras del código postal, o «?».</summary>
        public string Zona { get; set; }
        /// <summary>Hora del día en horas con decimales (10,5 = 10:30).</summary>
        public float Hora { get; set; }
        /// <summary>1 lunes … 7 domingo.</summary>
        public float DiaSemana { get; set; }
        public string Repartidor { get; set; }
        /// <summary>Los minutos tecleados en la nota (servicio técnico); NaN si no se teclearon.</summary>
        public float MinutosTecleados { get; set; }
    }

    /// <summary>Una fila de entrenamiento: los rasgos y la etiqueta.</summary>
    public class FilaTiempoParada : RasgosTiempoParada
    {
        public float Label { get; set; }
    }

    public class PrediccionTiempoParada
    {
        [ColumnName("Score")]
        public float Score { get; set; }
    }

    /// <summary>Un ejemplo ya calculado de una fila de RepartoParadasTiempos.</summary>
    public class EjemploTiempoParada
    {
        public TiempoParadaReparto Fila { get; set; }
        public RasgosTiempoParada Rasgos { get; set; }
        public DateTime Dia { get; set; }
        /// <summary>El objetivo: (fin de esta − fin de la anterior) − conducción estimada entre las dos posiciones. Nunca negativo.</summary>
        public double Minutos { get; set; }
        /// <summary>El segundo objetivo/registro: fin − llegada (si la PDA mandó la llegada y cae dentro del intervalo). Null si no.</summary>
        public double? MinutosDesdeLlegada { get; set; }
        /// <summary>Lo que daba la regla (MinutosPrevistos guardado, o la regla de hoy si no se guardó).</summary>
        public double MinutosRegla { get; set; }
    }

    /// <summary>
    /// NestoAPI#881 (corte 3): de las filas de tiempos reales a ejemplos del modelo. PURO. Descarta la primera parada del día (sin anterior),
    /// los intervalos de más de <see cref="MAXIMO_INTERVALO"/> minutos (comida, avería…) o no positivos, y las que no tienen las dos
    /// posiciones (sin ellas no se puede quitar la conducción). La conducción entre las dos posiciones se estima como el respaldo del fin de
    /// ruta (línea recta × 1,3 a 30 km/h) con el colchón de las constantes: así el objetivo es comparable con la regla.
    /// </summary>
    public static class ConstructorRasgosTiemposParada
    {
        public const double MAXIMO_INTERVALO = 120;
        public const string NINGUNO = "(ninguno)";
        public const string SIN_ZONA = "?";

        public static RasgosTiempoParada Rasgos(string tipoParada, string servicioTecnico, int? bultos, string formaCobro, decimal? importeCobro,
            string codigoPostal, DateTime hora, string repartidor, int? minutosTecleados)
        {
            bool llevaCobro = !string.IsNullOrWhiteSpace(formaCobro) || (importeCobro.HasValue && importeCobro.Value > 0);
            return new RasgosTiempoParada
            {
                TipoParada = string.IsNullOrWhiteSpace(tipoParada) ? TiposParadaReparto.ENVIO : tipoParada.Trim(),
                ServicioTecnico = string.IsNullOrWhiteSpace(servicioTecnico) ? NINGUNO : servicioTecnico.Trim().ToUpperInvariant(),
                Bultos = bultos.HasValue ? bultos.Value : float.NaN,
                LlevaCobro = llevaCobro ? 1 : 0,
                FormaCobro = string.IsNullOrWhiteSpace(formaCobro) ? NINGUNO : formaCobro.Trim(),
                Zona = Zona(codigoPostal),
                Hora = (float)hora.TimeOfDay.TotalHours,
                DiaSemana = hora.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)hora.DayOfWeek,
                Repartidor = string.IsNullOrWhiteSpace(repartidor) ? NINGUNO : ServicioRepartoPropio.NombreRepartidor(repartidor).ToUpperInvariant(),
                MinutosTecleados = minutosTecleados.HasValue && minutosTecleados.Value > 0 ? minutosTecleados.Value : float.NaN
            };
        }

        public static string Zona(string codigoPostal)
        {
            string cp = new string((codigoPostal ?? string.Empty).Where(char.IsDigit).ToArray());
            return cp.Length >= 3 ? cp.Substring(0, 3) : SIN_ZONA;
        }

        public static List<EjemploTiempoParada> Construir(IEnumerable<TiempoParadaReparto> filas, ConstantesTiemposReparto constantes)
        {
            constantes = constantes ?? ConstantesTiemposReparto.PorDefecto();
            var ejemplos = new List<EjemploTiempoParada>();
            foreach (TiempoParadaReparto f in filas ?? Enumerable.Empty<TiempoParadaReparto>())
            {
                EjemploTiempoParada e = Ejemplo(f, constantes);
                if (e != null)
                {
                    ejemplos.Add(e);
                }
            }
            return ejemplos;
        }

        /// <summary>Null si la fila no vale como ejemplo.</summary>
        public static EjemploTiempoParada Ejemplo(TiempoParadaReparto f, ConstantesTiemposReparto constantes)
        {
            if (f == null || !f.FechaHoraAnterior.HasValue || f.FechaHoraAnterior.Value.Date != f.FechaHoraFin.Date)
            {
                return null;
            }
            double intervalo = (f.FechaHoraFin - f.FechaHoraAnterior.Value).TotalMinutes;
            if (intervalo <= 0 || intervalo > MAXIMO_INTERVALO)
            {
                return null;
            }
            if (!CoordenadasGeo.Validas(f.Latitud, f.Longitud) || !CoordenadasGeo.Validas(f.LatitudAnterior, f.LongitudAnterior))
            {
                return null;
            }
            double conduccion = ConduccionEstimada(new CoordenadasGeo(f.LatitudAnterior.Value, f.LongitudAnterior.Value),
                new CoordenadasGeo(f.Latitud.Value, f.Longitud.Value), constantes);
            double? desdeLlegada = null;
            if (f.FechaHoraLlegada.HasValue && f.FechaHoraLlegada.Value >= f.FechaHoraAnterior.Value && f.FechaHoraLlegada.Value <= f.FechaHoraFin)
            {
                desdeLlegada = (f.FechaHoraFin - f.FechaHoraLlegada.Value).TotalMinutes;
            }
            string tipoRegla = f.TipoParada == TiempoParadaReparto.TIPO_RECOGIDA_REPOSICION ? TiposParadaReparto.REPOSICION : f.TipoParada;
            double regla = f.MinutosPrevistos.HasValue ? (double)f.MinutosPrevistos.Value
                : MinutosParadaReparto.De(new ParadaParaTiempo
                {
                    TipoParada = tipoRegla,
                    ConServicioTecnico = !string.IsNullOrWhiteSpace(f.ServicioTecnico),
                    MinutosServicio = f.MinutosServicioTecleados
                }, constantes);
            return new EjemploTiempoParada
            {
                Fila = f,
                Dia = f.Dia.Date,
                Minutos = Math.Max(0, intervalo - conduccion),
                MinutosDesdeLlegada = desdeLlegada,
                MinutosRegla = regla,
                Rasgos = Rasgos(f.TipoParada, f.ServicioTecnico, f.Bultos, f.FormaCobro, f.ImporteCobro, f.CodigoPostal, f.FechaHoraFin, f.Repartidor,
                    f.MinutosServicioTecleados)
            };
        }

        /// <summary>Minutos de conducción estimados entre dos puntos: línea recta × 1,3 a 30 km/h, con el colchón. PURO.</summary>
        public static double ConduccionEstimada(CoordenadasGeo de, CoordenadasGeo a, ConstantesTiemposReparto constantes)
            => HeuristicaRutas.DistanciaKm(de, a) * HeuristicaRutas.FACTOR_CARRETERA / ServicioRepartoPropio.VELOCIDAD_MEDIA_KMH * 60d
               * (constantes ?? ConstantesTiemposReparto.PorDefecto()).FactorConduccion;

        internal static string Num(double v) => v.ToString("0.##", CultureInfo.GetCultureInfo("es-ES"));
    }

    /// <summary>NestoAPI#881: el error medio (|real − previsto|, en minutos) de la regla y del modelo en un conjunto. PURO.</summary>
    public static class MetricasTiemposParada
    {
        public static double? ErrorMedio(IEnumerable<(double Real, double Previsto)> pares)
        {
            List<(double Real, double Previsto)> lista = (pares ?? Enumerable.Empty<(double, double)>()).ToList();
            return lista.Count == 0 ? (double?)null : Math.Round(lista.Average(p => Math.Abs(p.Real - p.Previsto)), 2);
        }

        /// <summary>«El modelo acierta mejor que la regla (6,1 min de error frente a 9,4)…». Null-safe.</summary>
        public static string Veredicto(int validacion, double? errorRegla, double? errorModelo)
        {
            if (!errorRegla.HasValue || !errorModelo.HasValue)
            {
                return "Sin datos suficientes: manda la regla de constantes.";
            }
            string r = ConstructorRasgosTiemposParada.Num(errorRegla.Value);
            string m = ConstructorRasgosTiemposParada.Num(errorModelo.Value);
            if (Math.Abs(errorRegla.Value - errorModelo.Value) < 0.05)
            {
                return $"El modelo y la regla aciertan igual ({m} min de error medio) en {validacion} paradas de validación.";
            }
            return errorModelo.Value < errorRegla.Value
                ? $"El modelo acierta mejor que la regla: {m} min de error medio frente a {r}, en {validacion} paradas de validación."
                : $"La regla acierta mejor que el modelo: {r} min de error medio frente a {m}, en {validacion} paradas de validación.";
        }
    }
}
