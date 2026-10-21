using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Stocks.Tendencias
{
    /// <summary>Contexto de 5 años: cinco bloques de 12 meses hacia atrás desde el mes de referencia.</summary>
    public class ContextoCincoAnios
    {
        /// <summary>Año (del último mes del bloque) del bloque de 12 meses con más venta.</summary>
        public int? AnioPico { get; set; }
        /// <summary>Últimos 12 meses ÷ bloque pico × 100.</summary>
        public decimal? PorcentajeDelPico { get; set; }
        /// <summary>0 = el pico son los últimos 12 meses.</summary>
        public int? AniosDesdePico { get; set; }
        public int[] VentasPorAnio { get; set; } = new int[0];
    }

    /// <summary>
    /// NestoAPI#630: cálculo puro sobre series mensuales (sin BD). Todas las funciones reciben la serie y el índice del último
    /// mes que cuenta (<paramref name="fin"/>), para poder calcular «como si fuera» un mes pasado en la calibración.
    /// </summary>
    public static class CalculadoraSeries
    {
        /// <summary>Media de los <paramref name="meses"/> que acaban en <paramref name="fin"/>. Los meses antes del 0 cuentan como 0.</summary>
        public static decimal Media(int[] serie, int fin, int meses)
        {
            if (serie == null || meses <= 0 || fin < 0)
            {
                return 0m;
            }
            return (decimal)Suma(serie, fin, meses) / meses;
        }

        public static int Suma(int[] serie, int fin, int meses)
        {
            int total = 0;
            for (int i = Math.Max(0, fin - meses + 1); i <= fin && i < serie.Length; i++)
            {
                total += serie[i];
            }
            return total;
        }

        /// <summary>
        /// Venta media ponderada: (3 × media de 3 meses + 2 × media de 6 + 1 × media de 12) ÷ 6. Reacciona antes que la media de
        /// 24 meses de ControlStockBase (la épica: «en un producto en declive reacciona un año tarde»), sin saltar con un mes raro.
        /// </summary>
        public static decimal MediaPonderada(int[] serie, int fin)
            => (3m * Media(serie, fin, 3) + 2m * Media(serie, fin, 6) + Media(serie, fin, 12)) / 6m;

        /// <summary>
        /// NestoAPI#632: media de los <paramref name="meses"/> que acaban en <paramref name="fin"/> sin la estacionalidad: cada mes
        /// se divide por el índice de la familia de su mes del año (<paramref name="indices"/>, enero primero) antes de promediar.
        /// Así tres meses de temporada alta no inflan la base (que luego se multiplica por el factor de los próximos meses) y
        /// tres de temporada baja no la hunden. Sin índices (familia sin 3 años), la media tal cual. Los meses antes del 0, 0.
        /// </summary>
        public static decimal MediaDesestacionalizada(int[] serie, int fin, int meses, decimal[] indices, DateTime mesFin)
        {
            if (indices == null)
            {
                return Media(serie, fin, meses);
            }
            if (serie == null || meses <= 0 || fin < 0)
            {
                return 0m;
            }
            decimal suma = 0m;
            for (int i = Math.Max(0, fin - meses + 1); i <= fin && i < serie.Length; i++)
            {
                decimal indice = indices[MesDelAnio(mesFin, fin, i)];
                suma += serie[i] / (indice <= 0m ? 1m : indice);
            }
            return suma / meses;
        }

        /// <summary>
        /// Índice estacional de la familia por mes del año (posición 0 = enero): venta media de ese mes en los últimos 3 años ÷
        /// venta media mensual de los 36. Null (sin estacionalidad) si la familia no tiene 3 años completos de venta (algún mes
        /// con venta en el primer año de la ventana o antes), si no llega a <paramref name="unidadesMinimas"/> o si no vende nada.
        /// Cada índice se acota a [0,3; 3] para que un mes raro no dispare un máximo.
        /// </summary>
        public static decimal[] IndicesEstacionales(int[] familia, int fin, DateTime mesFin, int unidadesMinimas, int? primerMesConVenta)
        {
            const int MESES = 36;
            if (familia == null || fin - MESES + 1 < 0 || !primerMesConVenta.HasValue || primerMesConVenta.Value > fin - MESES + 1)
            {
                return null;
            }
            int total = Suma(familia, fin, MESES);
            if (total <= 0 || total < unidadesMinimas)
            {
                return null;
            }
            decimal mediaMensual = (decimal)total / MESES;
            decimal[] sumaPorMes = new decimal[12];
            int[] cuenta = new int[12];
            for (int i = fin - MESES + 1; i <= fin; i++)
            {
                int mes = MesDelAnio(mesFin, fin, i);
                sumaPorMes[mes] += familia[i];
                cuenta[mes]++;
            }
            decimal[] indices = new decimal[12];
            for (int m = 0; m < 12; m++)
            {
                decimal media = cuenta[m] == 0 ? mediaMensual : sumaPorMes[m] / cuenta[m];
                indices[m] = Math.Min(3m, Math.Max(0.3m, media / mediaMensual));
            }
            return indices;
        }

        /// <summary>Mes del año (0 = enero) de la posición <paramref name="i"/> si la posición <paramref name="fin"/> es <paramref name="mesFin"/>.</summary>
        public static int MesDelAnio(DateTime mesFin, int fin, int i) => mesFin.AddMonths(i - fin).Month - 1;

        /// <summary>
        /// Pendiente relativa en % al mes: regresión lineal (mínimos cuadrados) de los 12 meses que acaban en <paramref name="fin"/>,
        /// desestacionalizados (venta ÷ índice del mes; sin índices, tal cual), dividida por la venta media desestacionalizada.
        /// Comparable entre productos: −5 quiere decir que cada mes vende un 5 % menos que la media. Null si no hay 12 meses o la
        /// media es 0.
        /// </summary>
        public static decimal? PendienteRelativa(int[] serie, int fin, decimal[] indices, DateTime mesFin)
        {
            const int MESES = 12;
            if (serie == null || fin - MESES + 1 < 0)
            {
                return null;
            }
            double[] y = new double[MESES];
            for (int k = 0; k < MESES; k++)
            {
                int i = fin - MESES + 1 + k;
                double indice = indices == null ? 1d : (double)indices[MesDelAnio(mesFin, fin, i)];
                y[k] = serie[i] / (indice <= 0 ? 1d : indice);
            }
            double media = y.Average();
            if (media <= 0)
            {
                return null;
            }
            double mediaX = (MESES - 1) / 2d;
            double numerador = 0, denominador = 0;
            for (int k = 0; k < MESES; k++)
            {
                numerador += (k - mediaX) * (y[k] - media);
                denominador += (k - mediaX) * (k - mediaX);
            }
            double pendiente = numerador / denominador;
            return Math.Round((decimal)(pendiente / media * 100d), 2);
        }

        /// <summary>Último trimestre ÷ mismo trimestre del año anterior. Null si el del año anterior es 0.</summary>
        public static decimal? RatioTrimestre(int[] serie, int fin)
        {
            if (serie == null || fin - 14 < 0)
            {
                return null;
            }
            int actual = Suma(serie, fin, 3);
            int anterior = Suma(serie, fin - 12, 3);
            return anterior <= 0 ? (decimal?)null : Math.Round((decimal)actual / anterior, 3);
        }

        /// <summary>Contexto de 5 años en bloques de 12 meses: año pico, % del pico y años desde el pico.</summary>
        public static ContextoCincoAnios Contexto(int[] serie, int fin, DateTime mesFin)
        {
            List<int> bloques = new List<int>();
            for (int b = 0; b < 5 && fin - 12 * b - 11 >= 0; b++)
            {
                bloques.Add(Suma(serie, fin - 12 * b, 12));
            }
            ContextoCincoAnios contexto = new ContextoCincoAnios { VentasPorAnio = bloques.ToArray() };
            if (bloques.Count == 0 || bloques.Max() <= 0)
            {
                return contexto;
            }
            int maximo = bloques.Max();
            int pico = bloques.IndexOf(maximo); // el más reciente si hay empate
            contexto.AniosDesdePico = pico;
            contexto.AnioPico = mesFin.AddMonths(-12 * pico).Year;
            contexto.PorcentajeDelPico = Math.Round(100m * bloques[0] / maximo, 1);
            return contexto;
        }

        /// <summary>Meses (enteros) desde <paramref name="desde"/> hasta <paramref name="hasta"/>, por días (30,44 al mes).</summary>
        public static int MesesEntre(DateTime desde, DateTime hasta)
            => hasta <= desde ? 0 : (int)Math.Floor((hasta - desde).TotalDays / 30.44);

        /// <summary>
        /// Velocidad de salida en uds/mes desde la primera venta: lo vendido desde entonces ÷ meses transcurridos (mínimo 1, para
        /// que un lanzamiento de hace dos semanas no se multiplique por dos).
        /// </summary>
        public static decimal Velocidad(int[] serie, int fin, DateTime fechaPrimeraVenta, DateTime fechaCorte)
        {
            double dias = (fechaCorte - fechaPrimeraVenta).TotalDays;
            decimal meses = Math.Max(1m, (decimal)(dias / 30.44));
            int mesesSerie = (int)Math.Ceiling(meses) + 1;
            return Math.Round(Suma(serie, fin, mesesSerie) / meses, 2);
        }

        /// <summary>Días de un mes medio (los mismos que usan <see cref="MesesEntre"/> y <see cref="Velocidad"/>).</summary>
        public const decimal DIAS_MES = 30.44m;

        /// <summary>NestoAPI#678: días naturales entre dos fechas (sin la hora); 0 si <paramref name="hasta"/> no es posterior.</summary>
        public static int DiasEntre(DateTime desde, DateTime hasta)
            => Math.Max(0, (int)(hasta.Date - desde.Date).TotalDays);

        /// <summary>
        /// NestoAPI#678: venta neta de los días en [<paramref name="desde"/>, <paramref name="hasta"/>), recortada a 0 (más
        /// devoluciones que ventas no es venta negativa, como en los meses).
        /// </summary>
        public static int SumaDiaria(IEnumerable<VentaDiariaTendencia> ventas, DateTime desde, DateTime hasta)
            => ventas == null ? 0 : Math.Max(0, ventas.Where(v => v.Fecha >= desde.Date && v.Fecha < hasta.Date).Sum(v => v.Cantidad));

        /// <summary>NestoAPI#678: <paramref name="unidades"/> vendidas en <paramref name="dias"/> días, en uds/mes (× 30,44 ÷ días).</summary>
        public static decimal VelocidadPorDias(int unidades, int dias)
            => dias <= 0 || unidades <= 0 ? 0m : Math.Round(unidades * DIAS_MES / dias, 2);

        /// <summary>
        /// NestoAPI#678: días de la ventana reciente de un nuevo de <paramref name="edadDias"/> días: la mitad de su vida como mucho
        /// (para que antes quede al menos otro tanto con el que comparar), hasta <paramref name="ventanaMaxima"/>; 0 (no se mira) si
        /// no llega a 7 días.
        /// </summary>
        public static int DiasVentanaReciente(int edadDias, int ventanaMaxima)
        {
            const int MINIMO = 7;
            int ventana = Math.Min(ventanaMaxima, edadDias / 2);
            return ventana > 0 && ventana >= Math.Min(MINIMO, ventanaMaxima) ? ventana : 0;
        }

        /// <summary>
        /// Tasa de recompra (%): de los clientes que compraron en los primeros <paramref name="diasLanzamiento"/> días desde la
        /// primera venta, cuántos han vuelto a comprar al menos <paramref name="diasRecompra"/> días después de su primera compra.
        /// Null si aún no ha pasado el tiempo para verlo (lanzamiento + recompra) o si no hay clientes.
        /// <para>Ojo: Amazon y la web son un solo cliente cada uno; cuentan como uno.</para>
        /// </summary>
        public static decimal? TasaRecompra(IEnumerable<VentaClienteTendencia> ventas, DateTime fechaPrimeraVenta, DateTime fechaCorte,
            int diasLanzamiento = 60, int diasRecompra = 30)
        {
            if (ventas == null || (fechaCorte - fechaPrimeraVenta).TotalDays < diasLanzamiento + diasRecompra)
            {
                return null;
            }
            var porCliente = ventas
                .Where(v => !string.IsNullOrWhiteSpace(v.Cliente) && v.Fecha < fechaCorte)
                .GroupBy(v => v.Cliente.Trim())
                .Select(g => g.Select(v => v.Fecha).OrderBy(f => f).ToList())
                .Where(f => f[0] <= fechaPrimeraVenta.AddDays(diasLanzamiento))
                .ToList();
            if (porCliente.Count == 0)
            {
                return null;
            }
            int repiten = porCliente.Count(f => f.Any(x => x >= f[0].AddDays(diasRecompra)));
            return Math.Round(100m * repiten / porCliente.Count, 1);
        }

        /// <summary>Índice del último mes con venta, o null.</summary>
        public static int? UltimoMesConVenta(int[] serie, int fin)
        {
            for (int i = Math.Min(fin, serie.Length - 1); i >= 0; i--)
            {
                if (serie[i] > 0)
                {
                    return i;
                }
            }
            return null;
        }

        public static int? PrimerMesConVenta(int[] serie)
        {
            for (int i = 0; i < serie.Length; i++)
            {
                if (serie[i] > 0)
                {
                    return i;
                }
            }
            return null;
        }
    }
}
