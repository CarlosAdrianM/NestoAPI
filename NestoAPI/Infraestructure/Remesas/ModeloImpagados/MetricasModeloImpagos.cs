using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Remesas.ModeloImpagados
{
    /// <summary>Un tramo de probabilidad prevista: lo previsto frente a lo que pasó.</summary>
    public class TramoCalibracionImpagos
    {
        public string Tramo { get; set; }
        /// <summary>Límite inferior del tramo (incluido), en tanto por uno.</summary>
        public double Desde { get; set; }
        /// <summary>Límite superior (no incluido); 1 en el último.</summary>
        public double Hasta { get; set; }
        public int Recibos { get; set; }
        public int Devueltos { get; set; }
        /// <summary>Probabilidad media prevista, en % (null sin recibos).</summary>
        public double? PrevistoPorcentaje { get; set; }
        /// <summary>% que volvió de verdad (null sin recibos).</summary>
        public double? RealPorcentaje { get; set; }
    }

    /// <summary>NestoAPI#896: cómo acierta una forma de prever la devolución (LA REGLA o el modelo) en un conjunto de recibos.</summary>
    public class MetricasImpagos
    {
        public int Recibos { get; set; }
        public int Devueltos { get; set; }
        /// <summary>Área bajo la curva ROC (0,5 = al azar; 1 = perfecto). Null sin devueltos o sin no devueltos.</summary>
        public double? Auc { get; set; }
        /// <summary>% de todas las devoluciones que cae en el 5 % de recibos con más riesgo previsto.</summary>
        public double? Captado5 { get; set; }
        /// <summary>Lo mismo en el 10 % con más riesgo.</summary>
        public double? Captado10 { get; set; }
        /// <summary>
        /// En los tramos altos (previsto ≥ <see cref="CalculoMetricasImpagos.UMBRAL_ALTOS"/>): devueltos reales / devueltos previstos
        /// (1 = clavado; 1,25 = vuelve un 25 % más de lo previsto). Null sin recibos en esos tramos.
        /// </summary>
        public double? CalibracionAltos { get; set; }
        public int RecibosAltos { get; set; }
        public List<TramoCalibracionImpagos> Tramos { get; set; } = new List<TramoCalibracionImpagos>();
    }

    /// <summary>NestoAPI#896: las métricas. PURA.</summary>
    public static class CalculoMetricasImpagos
    {
        /// <summary>Los cuatro tramos de medidas que decidió Carlos (24/11/26): &lt; 5 %, 5-15 %, 15-30 % y ≥ 30 %.</summary>
        internal static readonly double[] LimitesTramos = { 0, 0.05, 0.15, 0.30, 1.0000001 };
        internal static readonly string[] NombresTramos = { "< 5 %", "5-15 %", "15-30 %", "≥ 30 %" };
        /// <summary>«Tramos altos» = los dos de arriba (de llamar al cliente en adelante).</summary>
        public const double UMBRAL_ALTOS = 0.15;

        public static MetricasImpagos Calcular(IEnumerable<(double Probabilidad, bool Devuelto)> pares)
        {
            List<(double Probabilidad, bool Devuelto)> lista = (pares ?? Enumerable.Empty<(double, bool)>())
                .Where(p => !double.IsNaN(p.Probabilidad)).ToList();
            var metricas = new MetricasImpagos { Recibos = lista.Count, Devueltos = lista.Count(p => p.Devuelto) };
            for (int i = 0; i < NombresTramos.Length; i++)
            {
                double desde = LimitesTramos[i];
                double hasta = LimitesTramos[i + 1];
                List<(double Probabilidad, bool Devuelto)> grupo = lista.Where(p => p.Probabilidad >= desde && p.Probabilidad < hasta).ToList();
                metricas.Tramos.Add(new TramoCalibracionImpagos
                {
                    Tramo = NombresTramos[i],
                    Desde = desde,
                    Hasta = Math.Min(1, hasta),
                    Recibos = grupo.Count,
                    Devueltos = grupo.Count(p => p.Devuelto),
                    PrevistoPorcentaje = grupo.Count == 0 ? (double?)null : Math.Round(100 * grupo.Average(p => p.Probabilidad), 2),
                    RealPorcentaje = grupo.Count == 0 ? (double?)null : Math.Round(100.0 * grupo.Count(p => p.Devuelto) / grupo.Count, 2)
                });
            }
            if (lista.Count == 0)
            {
                return metricas;
            }
            metricas.Auc = Auc(lista);
            metricas.Captado5 = Captado(lista, 0.05);
            metricas.Captado10 = Captado(lista, 0.10);
            List<(double Probabilidad, bool Devuelto)> altos = lista.Where(p => p.Probabilidad >= UMBRAL_ALTOS).ToList();
            metricas.RecibosAltos = altos.Count;
            double previstos = altos.Sum(p => p.Probabilidad);
            metricas.CalibracionAltos = altos.Count == 0 || previstos <= 0 ? (double?)null : Math.Round(altos.Count(p => p.Devuelto) / previstos, 4);
            return metricas;
        }

        /// <summary>AUC por rangos (los empates comparten el rango medio). Null si falta una de las dos clases.</summary>
        public static double? Auc(IReadOnlyList<(double Probabilidad, bool Devuelto)> pares)
        {
            long positivos = pares.Count(p => p.Devuelto);
            long negativos = pares.Count - positivos;
            if (positivos == 0 || negativos == 0)
            {
                return null;
            }
            List<(double Probabilidad, bool Devuelto)> orden = pares.OrderBy(p => p.Probabilidad).ToList();
            double sumaRangos = 0;
            int i = 0;
            while (i < orden.Count)
            {
                int j = i;
                int positivosDelEmpate = 0;
                while (j < orden.Count && orden[j].Probabilidad == orden[i].Probabilidad)
                {
                    if (orden[j].Devuelto)
                    {
                        positivosDelEmpate++;
                    }
                    j++;
                }
                sumaRangos += (i + j + 1) / 2.0 * positivosDelEmpate;
                i = j;
            }
            return Math.Round((sumaRangos - positivos * (positivos + 1) / 2.0) / (positivos * (double)negativos), 4);
        }

        /// <summary>% de las devoluciones que está en la fracción de recibos con más probabilidad prevista (al menos un recibo).</summary>
        public static double? Captado(IReadOnlyList<(double Probabilidad, bool Devuelto)> pares, double fraccion)
        {
            int devueltos = pares.Count(p => p.Devuelto);
            if (devueltos == 0)
            {
                return null;
            }
            int cuantos = Math.Max(1, (int)(pares.Count * fraccion));
            return Math.Round(100.0 * pares.OrderByDescending(p => p.Probabilidad).Take(cuantos).Count(p => p.Devuelto) / devueltos, 2);
        }

        internal static string Texto(MetricasImpagos m) => m == null ? "sin datos"
            : $"AUC {Num(m.Auc)}, recoge el {Num(m.Captado5)} % de las devoluciones en el 5 % más arriesgado y el {Num(m.Captado10)} % en el 10 %, " +
              $"calibración en los tramos altos {Num(m.CalibracionAltos)}";

        internal static string Num(double? v) => v.HasValue ? v.Value.ToString("0.###", CultureInfo.GetCultureInfo("es-ES")) : "—";
    }

    /// <summary>
    /// NestoAPI#896: calibración de Platt (probabilidad = sigmoide de A × puntuación + B), ajustada por Newton con los objetivos
    /// suavizados de Platt. PURA. Se ajusta con recibos que el árbol NO ha visto.
    /// </summary>
    public class CalibradorPlatt
    {
        public double A { get; set; } = 1;
        public double B { get; set; }

        public double Probabilidad(double puntuacion)
        {
            double z = A * puntuacion + B;
            return z >= 0 ? 1 / (1 + Math.Exp(-z)) : Math.Exp(z) / (1 + Math.Exp(z));
        }

        public static CalibradorPlatt Ajustar(IEnumerable<(double Puntuacion, bool Devuelto)> pares)
        {
            List<(double Puntuacion, bool Devuelto)> lista = (pares ?? Enumerable.Empty<(double, bool)>())
                .Where(p => !double.IsNaN(p.Puntuacion) && !double.IsInfinity(p.Puntuacion)).ToList();
            int positivos = lista.Count(p => p.Devuelto);
            int negativos = lista.Count - positivos;
            if (positivos == 0 || negativos == 0)
            {
                return new CalibradorPlatt();
            }
            double objetivoSi = (positivos + 1.0) / (positivos + 2.0);
            double objetivoNo = 1.0 / (negativos + 2.0);
            double a = 0;
            double b = Math.Log((positivos + 1.0) / (negativos + 1.0));
            for (int iteracion = 0; iteracion < 100; iteracion++)
            {
                double ga = 0, gb = 0, haa = 1e-9, hab = 0, hbb = 1e-9;
                foreach ((double s, bool devuelto) in lista)
                {
                    double z = a * s + b;
                    double p = z >= 0 ? 1 / (1 + Math.Exp(-z)) : Math.Exp(z) / (1 + Math.Exp(z));
                    double d = p - (devuelto ? objetivoSi : objetivoNo);
                    double w = Math.Max(p * (1 - p), 1e-12);
                    ga += d * s;
                    gb += d;
                    haa += w * s * s;
                    hab += w * s;
                    hbb += w;
                }
                double det = haa * hbb - hab * hab;
                if (Math.Abs(det) < 1e-18)
                {
                    break;
                }
                double da = (hbb * ga - hab * gb) / det;
                double db = (haa * gb - hab * ga) / det;
                a -= da;
                b -= db;
                if (Math.Abs(da) < 1e-9 && Math.Abs(db) < 1e-9)
                {
                    break;
                }
            }
            return double.IsNaN(a) || double.IsNaN(b) || double.IsInfinity(a) || double.IsInfinity(b)
                ? new CalibradorPlatt()
                : new CalibradorPlatt { A = a, B = b };
        }
    }

    public class DecisionPuertaImpagos
    {
        public bool Promover { get; set; }
        public string Motivo { get; set; }
    }

    /// <summary>
    /// NestoAPI#896: puerta de calidad del modelo de impagados. PURA. El nuevo solo sustituye al guardado si en validación no ordena
    /// peor que LA REGLA simple (su AUC como mucho <see cref="TOLERANCIA_AUC"/> por debajo) y no empeora al modelo en uso (AUC y % de
    /// devoluciones recogidas en el 10 % más arriesgado, con la holgura del ruido entre dos ventanas de validación). Sin modelo
    /// guardado, basta con no ser peor que la regla.
    /// </summary>
    public static class PuertaCalidadImpagos
    {
        public const int MINIMO_DEVUELTOS = 30;
        public const double TOLERANCIA_AUC = 0.01;
        public const double TOLERANCIA_CAPTADO = 3.0;

        public static DecisionPuertaImpagos Decidir(MetricasImpagos activo, bool hayModeloGuardado, MetricasImpagos nuevo, MetricasImpagos regla)
        {
            if (nuevo?.Auc == null || nuevo.Devueltos < MINIMO_DEVUELTOS)
            {
                return new DecisionPuertaImpagos { Promover = false, Motivo = $"pocas devoluciones en validación ({nuevo?.Devueltos ?? 0}; hacen falta {MINIMO_DEVUELTOS})" };
            }
            if (regla?.Auc != null && nuevo.Auc.Value < regla.Auc.Value - TOLERANCIA_AUC - 1e-9)
            {
                return new DecisionPuertaImpagos { Promover = false, Motivo = $"ordena peor que la regla simple (AUC {Num(nuevo.Auc)} frente a {Num(regla.Auc)})" };
            }
            if (!hayModeloGuardado || activo?.Auc == null)
            {
                return new DecisionPuertaImpagos { Promover = true, Motivo = $"no había modelo guardado y no es peor que la regla (AUC {Num(nuevo.Auc)} frente a {Num(regla?.Auc)})" };
            }
            var fallos = new List<string>();
            if (nuevo.Auc.Value < activo.Auc.Value - TOLERANCIA_AUC - 1e-9)
            {
                fallos.Add($"ordena peor (AUC {Num(activo.Auc)} → {Num(nuevo.Auc)})");
            }
            if (activo.Captado10.HasValue && nuevo.Captado10.HasValue && nuevo.Captado10.Value < activo.Captado10.Value - TOLERANCIA_CAPTADO - 1e-9)
            {
                fallos.Add($"recoge menos devoluciones en el 10 % más arriesgado ({Num(activo.Captado10)} % → {Num(nuevo.Captado10)} %)");
            }
            return fallos.Any()
                ? new DecisionPuertaImpagos { Promover = false, Motivo = "empeora: " + string.Join(" y ", fallos) }
                : new DecisionPuertaImpagos { Promover = true, Motivo = $"no empeora (AUC {Num(activo.Auc)} → {Num(nuevo.Auc)}; regla {Num(regla?.Auc)})" };
        }

        private static string Num(double? v) => CalculoMetricasImpagos.Num(v);
    }

    /// <summary>Una predicción de la sombra ya resuelta (volvió o no).</summary>
    public class PrediccionResueltaImpago
    {
        public DateTime FechaGiro { get; set; }
        /// <summary>La del modelo (null si ese día no había modelo guardado).</summary>
        public double? Probabilidad { get; set; }
        public double? ProbabilidadRegla { get; set; }
        public bool Devuelto { get; set; }
    }

    /// <summary>Una semana de giros (de lunes a domingo) medida con la ventana que acaba en ella.</summary>
    public class SemanaFiabilidadImpagos
    {
        /// <summary>El lunes de la semana.</summary>
        public DateTime Semana { get; set; }
        /// <summary>Recibos girados esa semana con predicción del modelo.</summary>
        public int RecibosDeLaSemana { get; set; }
        /// <summary>Lo medido en la ventana de <see cref="ReglaFiabilidadImpagos.SEMANAS_VENTANA"/> semanas que acaba en esta.</summary>
        public int Recibos { get; set; }
        public int Devueltos { get; set; }
        public double? Auc { get; set; }
        public double? CalibracionAltos { get; set; }
        public int RecibosAltos { get; set; }
        public bool Cumple { get; set; }
        public string Motivo { get; set; }
    }

    public class FiabilidadImpagos
    {
        public bool Fiable { get; set; }
        /// <summary>El domingo de la semana que completó la racha vigente de semanas seguidas cumpliendo. Null si no es fiable.</summary>
        public DateTime? FiableDesde { get; set; }
        public int SemanasSeguidas { get; set; }
        public string Motivo { get; set; }
        public List<SemanaFiabilidadImpagos> Semanas { get; set; } = new List<SemanaFiabilidadImpagos>();
    }

    /// <summary>
    /// NestoAPI#896: cuándo es «fiable» el modelo en la sombra: AUC ≥ <see cref="AUC_MINIMO"/> y calibración dentro de
    /// ±<see cref="MARGEN_CALIBRACION"/> en los tramos altos durante <see cref="SEMANAS_SEGUIDAS"/> semanas seguidas. PURA.
    /// Una semana sola trae unas 15 devoluciones (no se puede medir nada con eso), así que cada semana CERRADA (todos sus giros
    /// tienen ya más de 45 días) se mide con la ventana de las <see cref="SEMANAS_VENTANA"/> semanas que acaban en ella, y además
    /// hacen falta <see cref="MINIMO_DEVUELTOS"/> devoluciones y <see cref="MINIMO_RECIBOS_ALTOS"/> recibos en los tramos altos.
    /// </summary>
    public static class ReglaFiabilidadImpagos
    {
        public const double AUC_MINIMO = 0.80;
        public const double MARGEN_CALIBRACION = 0.25;
        public const int SEMANAS_SEGUIDAS = 4;
        public const int SEMANAS_VENTANA = 12;
        public const int MINIMO_DEVUELTOS = 30;
        public const int MINIMO_RECIBOS_ALTOS = 30;

        public static DateTime Lunes(DateTime dia) => dia.Date.AddDays(-(((int)dia.DayOfWeek + 6) % 7));

        /// <param name="resueltas">Predicciones resueltas (las que no tienen probabilidad del modelo no cuentan).</param>
        /// <param name="semanas">Cuántas semanas cerradas se devuelven (las últimas).</param>
        public static FiabilidadImpagos Evaluar(IEnumerable<PrediccionResueltaImpago> resueltas, DateTime hoy, int semanas)
        {
            List<PrediccionResueltaImpago> lista = (resueltas ?? Enumerable.Empty<PrediccionResueltaImpago>())
                .Where(p => p != null && p.Probabilidad.HasValue).ToList();
            var resultado = new FiabilidadImpagos();
            if (lista.Count == 0)
            {
                resultado.Motivo = "Todavía no hay predicciones del modelo resueltas en la sombra.";
                return resultado;
            }
            // Última semana cerrada: su domingo + 45 días ya ha pasado.
            DateTime ultimoLunes = Lunes(hoy.Date.AddDays(-ConstructorRasgosImpagos.DIAS_ETIQUETA - 7));
            DateTime primerLunes = Lunes(lista.Min(p => p.FechaGiro));
            var todas = new List<SemanaFiabilidadImpagos>();
            for (DateTime lunes = primerLunes; lunes <= ultimoLunes; lunes = lunes.AddDays(7))
            {
                DateTime fin = lunes.AddDays(7);
                DateTime inicioVentana = fin.AddDays(-7 * SEMANAS_VENTANA);
                MetricasImpagos m = CalculoMetricasImpagos.Calcular(lista
                    .Where(p => p.FechaGiro.Date >= inicioVentana && p.FechaGiro.Date < fin)
                    .Select(p => (p.Probabilidad.Value, p.Devuelto)));
                var semana = new SemanaFiabilidadImpagos
                {
                    Semana = lunes,
                    RecibosDeLaSemana = lista.Count(p => p.FechaGiro.Date >= lunes && p.FechaGiro.Date < fin),
                    Recibos = m.Recibos,
                    Devueltos = m.Devueltos,
                    Auc = m.Auc,
                    CalibracionAltos = m.CalibracionAltos,
                    RecibosAltos = m.RecibosAltos
                };
                semana.Motivo = MotivoNoCumple(semana);
                semana.Cumple = semana.Motivo == null;
                todas.Add(semana);
            }
            if (todas.Count == 0)
            {
                resultado.Motivo = $"Todavía no hay ninguna semana cerrada (los giros tardan {ConstructorRasgosImpagos.DIAS_ETIQUETA} días en resolverse).";
                return resultado;
            }
            int racha = 0;
            for (int i = todas.Count - 1; i >= 0 && todas[i].Cumple; i--)
            {
                racha++;
            }
            resultado.SemanasSeguidas = racha;
            resultado.Fiable = racha >= SEMANAS_SEGUIDAS;
            if (resultado.Fiable)
            {
                resultado.FiableDesde = todas[todas.Count - racha + SEMANAS_SEGUIDAS - 1].Semana.AddDays(6);
                resultado.Motivo = $"Fiable: lleva {racha} semanas seguidas cumpliendo (AUC ≥ {Num(AUC_MINIMO)} y calibración dentro de ±{Num(100 * MARGEN_CALIBRACION)} % en los tramos altos).";
            }
            else
            {
                SemanaFiabilidadImpagos ultima = todas[todas.Count - 1];
                resultado.Motivo = racha > 0
                    ? $"Aún no: lleva {racha} de {SEMANAS_SEGUIDAS} semanas seguidas cumpliendo."
                    : $"Aún no: la semana del {ultima.Semana:dd/MM/yyyy} no cumple ({ultima.Motivo}).";
            }
            resultado.Semanas = todas.Skip(Math.Max(0, todas.Count - Math.Max(1, semanas))).ToList();
            return resultado;
        }

        internal static string MotivoNoCumple(SemanaFiabilidadImpagos s)
        {
            if (s.RecibosDeLaSemana == 0)
            {
                return "sin recibos con predicción del modelo esa semana";
            }
            if (s.Devueltos < MINIMO_DEVUELTOS)
            {
                return $"pocas devoluciones en la ventana ({s.Devueltos}; hacen falta {MINIMO_DEVUELTOS})";
            }
            if (!s.Auc.HasValue || s.Auc.Value < AUC_MINIMO)
            {
                return $"AUC {Num(s.Auc)} (hace falta {Num(AUC_MINIMO)})";
            }
            if (s.RecibosAltos < MINIMO_RECIBOS_ALTOS || !s.CalibracionAltos.HasValue)
            {
                return $"pocos recibos en los tramos altos ({s.RecibosAltos}; hacen falta {MINIMO_RECIBOS_ALTOS})";
            }
            return Math.Abs(s.CalibracionAltos.Value - 1) > MARGEN_CALIBRACION + 1e-9
                ? $"en los tramos altos vuelve {Num(s.CalibracionAltos)} veces lo previsto (fuera de ±{Num(100 * MARGEN_CALIBRACION)} %)"
                : null;
        }

        private static string Num(double? v) => CalculoMetricasImpagos.Num(v);
    }
}
