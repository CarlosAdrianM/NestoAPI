using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Calidad.Tablero
{
    // NestoAPI#914, corte «tablero de KPI con nota semanal» (idea de Carlos, 27/11/26; como los informes de Amazon): cada KPI tiene su
    // valor «ahora» y una nota por semana (POOR, FAIR, GOOD, FANTASTIC). Aquí, el catálogo: UNA CLASE POR KPI con la interfaz común; nada de
    // «si el KPI es X» fuera de su clase. Definiciones exactas en docs/claude/calidad-kpi.md, «Tablero de KPI con nota semanal».

    /// <summary>De quién es el valor de un KPI.</summary>
    public enum TipoDuenoKpi
    {
        Empresa,
        Almacen,
        Vendedor,
        Preparador
    }

    public enum UnidadKpi
    {
        Porcentaje,
        Euros,
        Unidades
    }

    public enum SentidoKpi
    {
        MasEsMejor,
        MenosEsMejor
    }

    /// <summary>Una semana (lunes a domingo) o lo que va de ella: [<see cref="Lunes"/>, <see cref="Hasta"/>).</summary>
    public class PeriodoKpi
    {
        public PeriodoKpi(DateTime lunes, DateTime? hasta = null)
        {
            Lunes = SemanaKpi.LunesDe(lunes);
            DateTime fin = Lunes.AddDays(7);
            Hasta = hasta.HasValue && hasta.Value.Date < fin ? hasta.Value.Date : fin;
        }

        public DateTime Lunes { get; }
        /// <summary>Exclusivo: el lunes siguiente si la semana está entera; mañana si está en curso.</summary>
        public DateTime Hasta { get; }
        /// <summary>El último día incluido.</summary>
        public DateTime UltimoDia => Hasta.AddDays(-1);
        public bool Entera => Hasta == Lunes.AddDays(7);
    }

    /// <summary>El valor de un KPI para un dueño en un periodo.</summary>
    public class ValorKpi
    {
        public TipoDuenoKpi TipoDueno { get; set; }
        public string Dueno { get; set; }
        /// <summary>Null = no hay base para calcularlo (p. ej. un porcentaje sin denominador).</summary>
        public decimal? Valor { get; set; }
        public decimal? Numerador { get; set; }
        public decimal? Denominador { get; set; }
        /// <summary>Una explicación corta, de usted (el motivo que más resta, lo que queda sin imputar…). ≤ 400.</summary>
        public string Detalle { get; set; }
    }

    /// <summary>Un KPI del tablero. Cada uno, su clase; el catálogo es <see cref="CatalogoKpiTablero"/>.</summary>
    public interface IKpiTablero
    {
        /// <summary>Clave estable (va en la BD y en las URL): no se cambia nunca.</summary>
        string Clave { get; }
        /// <summary>Nombre corto para enseñar.</summary>
        string Nombre { get; }
        /// <summary>Qué mide, de usted.</summary>
        string Descripcion { get; }
        /// <summary>De quién puede ser (un KPI puede darse por empresa y por almacén, p. ej.).</summary>
        IReadOnlyList<TipoDuenoKpi> TiposDueno { get; }
        UnidadKpi Unidad { get; }
        SentidoKpi Sentido { get; }
        /// <summary>0 = la semana se cierra al acabar (y hay valor en curso). N = hay que esperar N días después del domingo.</summary>
        int DiasMaduracion { get; }
        /// <summary>Por qué hay que esperar (null si no hay que esperar), de usted.</summary>
        string PorQueMadura { get; }
        /// <summary>False = todavía no hay de dónde leerlo (el tablero lo lista como «todavía no disponible»).</summary>
        bool TieneFuente { get; }
        /// <summary>Lo calcula para el periodo, para todos sus dueños (lee por <paramref name="fuentes"/>, por lotes y con NOLOCK).</summary>
        List<ValorKpi> Calcular(PeriodoKpi periodo, IFuentesKpi fuentes);
    }

    public static class SemanaKpi
    {
        /// <summary>El lunes de la semana de <paramref name="dia"/>.</summary>
        public static DateTime LunesDe(DateTime dia)
        {
            DateTime d = dia.Date;
            return d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
        }

        /// <summary>El día en que la semana del <paramref name="lunes"/> se puede dar por cerrada: el lunes siguiente + los días de maduración.</summary>
        public static DateTime CerradaEl(DateTime lunes, int diasMaduracion) => LunesDe(lunes).AddDays(7 + Math.Max(0, diasMaduracion));

        public static bool EstaCerrada(DateTime lunes, int diasMaduracion, DateTime hoy) => hoy.Date >= CerradaEl(lunes, diasMaduracion);

        /// <summary>La última semana ya cerrada a <paramref name="hoy"/> para esa maduración.</summary>
        public static DateTime UltimaCerrada(int diasMaduracion, DateTime hoy)
        {
            DateTime lunes = LunesDe(hoy).AddDays(-7);
            while (!EstaCerrada(lunes, diasMaduracion, hoy))
            {
                lunes = lunes.AddDays(-7);
            }
            return lunes;
        }

        internal static decimal? Porcentaje(decimal parte, decimal total) =>
            total <= 0 ? (decimal?)null : Math.Round(100m * parte / total, 1, MidpointRounding.AwayFromZero);

        internal static string Recortar(string texto, int maximo = 400) =>
            string.IsNullOrEmpty(texto) || texto.Length <= maximo ? texto : texto.Substring(0, maximo - 1) + "…";
    }

    /// <summary>El catálogo de KPI del tablero. Un KPI nuevo = una clase nueva aquí.</summary>
    public static class CatalogoKpiTablero
    {
        public static IReadOnlyList<IKpiTablero> Todos { get; } = new List<IKpiTablero>
        {
            new KpiPerdidaDesconocida(),
            new KpiDiferenciasReposicion(),
            new KpiDiferenciasInventario(),
            new KpiEntregasIncompletas(),
            new KpiPedidoPerfecto(),
            new KpiReenviadoSinCoste()
        };

        public static IKpiTablero Buscar(string clave) =>
            Todos.FirstOrDefault(k => string.Equals(k.Clave, clave?.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
