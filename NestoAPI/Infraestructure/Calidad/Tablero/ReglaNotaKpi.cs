using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Calidad.Tablero
{
    public enum NotaKpi
    {
        POOR,
        FAIR,
        GOOD,
        FANTASTIC
    }

    public enum TendenciaKpi
    {
        Sube,
        Baja,
        Igual
    }

    /// <summary>
    /// Los tres cortes de un KPI y tipo de dueño (dbo.CalidadKpiUmbrales), vigentes desde un lunes. Con «más es mejor» van de menor a mayor
    /// (POOR &lt; PoorFair ≤ FAIR &lt; FairGood ≤ GOOD &lt; GoodFantastic ≤ FANTASTIC); con «menos es mejor», de mayor a menor.
    /// </summary>
    public class UmbralesKpi
    {
        public string Kpi { get; set; }
        public TipoDuenoKpi TipoDueno { get; set; }
        public decimal CortePoorFair { get; set; }
        public decimal CorteFairGood { get; set; }
        public decimal CorteGoodFantastic { get; set; }
        public DateTime VigenteDesde { get; set; }
        public string Usuario { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }

    public class PropuestaUmbralesKpi
    {
        public decimal CortePoorFair { get; set; }
        public decimal CorteFairGood { get; set; }
        public decimal CorteGoodFantastic { get; set; }
    }

    /// <summary>
    /// NestoAPI#914, LA REGLA de la nota semanal (pura): valor + umbrales vigentes esa semana + sentido → POOR | FAIR | GOOD | FANTASTIC; sin
    /// umbrales (o sin valor), sin nota. La nota no se guarda: se saca siempre con el umbral VIGENTE ESA SEMANA, así que aprobar un umbral nuevo
    /// no reescribe las notas de las semanas pasadas.
    /// </summary>
    public static class ReglaNotaKpi
    {
        /// <summary>Semanas anteriores con las que se compara para la tendencia.</summary>
        public const int SEMANAS_TENDENCIA = 4;
        /// <summary>Por debajo de este cambio relativo (2 %) sobre la media, «igual».</summary>
        public const decimal TOLERANCIA_RELATIVA = 0.02m;
        /// <summary>Mínimo de semanas-dueño guardadas para proponer umbrales.</summary>
        public const int MINIMO_PARA_PROPONER = 8;
        /// <summary>«Desde siempre»: el primer umbral aprobado de un KPI vale también para su histórico.</summary>
        public static readonly DateTime DESDE_SIEMPRE = new DateTime(2000, 1, 3);

        public static NotaKpi? Nota(decimal? valor, UmbralesKpi umbrales, SentidoKpi sentido)
        {
            if (!valor.HasValue || umbrales == null)
            {
                return null;
            }
            decimal v = valor.Value;
            if (sentido == SentidoKpi.MasEsMejor)
            {
                return v < umbrales.CortePoorFair ? NotaKpi.POOR
                    : v < umbrales.CorteFairGood ? NotaKpi.FAIR
                    : v < umbrales.CorteGoodFantastic ? NotaKpi.GOOD
                    : NotaKpi.FANTASTIC;
            }
            return v > umbrales.CortePoorFair ? NotaKpi.POOR
                : v > umbrales.CorteFairGood ? NotaKpi.FAIR
                : v > umbrales.CorteGoodFantastic ? NotaKpi.GOOD
                : NotaKpi.FANTASTIC;
        }

        /// <summary>El umbral vigente la semana del <paramref name="lunes"/>: el de VigenteDesde más reciente que no sea posterior. Null si ninguno.</summary>
        public static UmbralesKpi Vigente(IEnumerable<UmbralesKpi> umbrales, string kpi, TipoDuenoKpi tipo, DateTime lunes) =>
            (umbrales ?? Enumerable.Empty<UmbralesKpi>())
                .Where(u => u != null && u.TipoDueno == tipo && string.Equals(u.Kpi, kpi, StringComparison.OrdinalIgnoreCase) && u.VigenteDesde.Date <= lunes.Date)
                .OrderByDescending(u => u.VigenteDesde)
                .ThenByDescending(u => u.FechaModificacion ?? DateTime.MinValue)
                .FirstOrDefault();

        /// <summary>
        /// Sube, baja o igual: el valor frente a la media de las (hasta) <see cref="SEMANAS_TENDENCIA"/> semanas anteriores con valor. Igual si cambia
        /// menos del 2 % de la media (o nada, si la media es 0). Null sin valor o sin ninguna semana anterior. Es del VALOR (no de si mejora: eso lo da el sentido).
        /// </summary>
        public static TendenciaKpi? Tendencia(decimal? valor, IEnumerable<decimal?> anteriores)
        {
            if (!valor.HasValue)
            {
                return null;
            }
            List<decimal> previas = (anteriores ?? Enumerable.Empty<decimal?>()).Where(v => v.HasValue).Select(v => v.Value).Take(SEMANAS_TENDENCIA).ToList();
            if (previas.Count == 0)
            {
                return null;
            }
            decimal media = previas.Average();
            decimal diferencia = valor.Value - media;
            decimal tolerancia = Math.Abs(media) * TOLERANCIA_RELATIVA;
            if (Math.Abs(diferencia) <= tolerancia)
            {
                return TendenciaKpi.Igual;
            }
            return diferencia > 0 ? TendenciaKpi.Sube : TendenciaKpi.Baja;
        }

        /// <summary>¿La tendencia es a mejor? Null si es igual o no hay.</summary>
        public static bool? Mejora(TendenciaKpi? tendencia, SentidoKpi sentido)
        {
            if (!tendencia.HasValue || tendencia.Value == TendenciaKpi.Igual)
            {
                return null;
            }
            return (tendencia.Value == TendenciaKpi.Sube) == (sentido == SentidoKpi.MasEsMejor);
        }

        /// <summary>
        /// Propone los tres cortes con los valores ya guardados: percentiles 25, 50 y 75 del sentido bueno (con «más es mejor», P25 = POOR/FAIR,
        /// P50 = FAIR/GOOD, P75 = GOOD/FANTASTIC; con «menos es mejor», P75, P50 y P25), redondeados a números legibles. Null si hay menos de
        /// <see cref="MINIMO_PARA_PROPONER"/> valores.
        /// </summary>
        public static PropuestaUmbralesKpi Proponer(IEnumerable<decimal> valores, SentidoKpi sentido)
        {
            List<decimal> ordenados = (valores ?? Enumerable.Empty<decimal>()).OrderBy(v => v).ToList();
            if (ordenados.Count < MINIMO_PARA_PROPONER)
            {
                return null;
            }
            decimal p25 = Legible(Percentil(ordenados, 0.25m));
            decimal p50 = Legible(Percentil(ordenados, 0.50m));
            decimal p75 = Legible(Percentil(ordenados, 0.75m));
            return sentido == SentidoKpi.MasEsMejor
                ? new PropuestaUmbralesKpi { CortePoorFair = p25, CorteFairGood = p50, CorteGoodFantastic = p75 }
                : new PropuestaUmbralesKpi { CortePoorFair = p75, CorteFairGood = p50, CorteGoodFantastic = p25 };
        }

        /// <summary>Percentil con interpolación lineal entre los dos más cercanos (como PERCENTIL.INC de Excel). La lista, ordenada.</summary>
        internal static decimal Percentil(IReadOnlyList<decimal> ordenados, decimal p)
        {
            if (ordenados.Count == 1)
            {
                return ordenados[0];
            }
            decimal posicion = p * (ordenados.Count - 1);
            int abajo = (int)Math.Floor(posicion);
            int arriba = Math.Min(abajo + 1, ordenados.Count - 1);
            return ordenados[abajo] + ((posicion - abajo) * (ordenados[arriba] - ordenados[abajo]));
        }

        /// <summary>
        /// Un número que se lee bien: de 10 a 100, entero (92,37 → 92); de 1 a 10, con un decimal; por debajo de 1, con dos; de 100 en adelante,
        /// con dos cifras significativas (1.234 → 1.200; 355 → 360).
        /// </summary>
        public static decimal Legible(decimal valor)
        {
            decimal a = Math.Abs(valor);
            int signo = valor < 0 ? -1 : 1;
            decimal r;
            if (a >= 100)
            {
                decimal escala = 1;
                while (a / escala >= 100)
                {
                    escala *= 10;
                }
                r = Math.Round(a / escala, 0, MidpointRounding.AwayFromZero) * escala;
            }
            else if (a >= 10)
            {
                r = Math.Round(a, 0, MidpointRounding.AwayFromZero);
            }
            else if (a >= 1)
            {
                r = Math.Round(a, 1, MidpointRounding.AwayFromZero);
            }
            else
            {
                r = Math.Round(a, 2, MidpointRounding.AwayFromZero);
            }
            return signo * r;
        }

        /// <summary>Los cortes están en orden para el sentido (iguales se admiten: esa nota se queda sin hueco). Devuelve el error de usted o null.</summary>
        public static string ValidarCortes(decimal poorFair, decimal fairGood, decimal goodFantastic, SentidoKpi sentido)
        {
            if (sentido == SentidoKpi.MasEsMejor)
            {
                return poorFair <= fairGood && fairGood <= goodFantastic ? null
                    : "En este indicador más es mejor: los cortes tienen que ir de menor a mayor (POOR/FAIR ≤ FAIR/GOOD ≤ GOOD/FANTASTIC).";
            }
            return poorFair >= fairGood && fairGood >= goodFantastic ? null
                : "En este indicador menos es mejor: los cortes tienen que ir de mayor a menor (POOR/FAIR ≥ FAIR/GOOD ≥ GOOD/FANTASTIC).";
        }
    }
}
