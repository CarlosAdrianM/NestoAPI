using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Remesas.ModeloImpagados
{
    /// <summary>
    /// NestoAPI#896: los pesos de LA REGLA ya ajustados (se guardan en JSON junto al modelo): log-odds de la tasa base y, por rasgo y
    /// tramo («UltimaDevolucion|2»), cuánto sube o baja.
    /// </summary>
    public class PesosReglaImpagos
    {
        public DateTime Fecha { get; set; }
        public int Ejemplos { get; set; }
        public double TasaBase { get; set; }
        public double LogOddsBase { get; set; }
        public Dictionary<string, double> Pesos { get; set; } = new Dictionary<string, double>();
    }

    /// <summary>
    /// NestoAPI#896: LA REGLA simple de probabilidad de devolución, la del análisis del 24/11/26 (prueba_modelo.py: AUC 0,85): suma de
    /// pesos por tramos. PURA. Cada rasgo se parte en tramos; el peso de un tramo es el log-odds de su tasa de devolución SUAVIZADA
    /// ((devueltos + <see cref="SUAVIZADO"/> × tasa base) / (recibos + <see cref="SUAVIZADO"/>)) menos el de la tasa base; la
    /// probabilidad es la sigmoide de la base + <see cref="ENCOGE"/> × la suma de los pesos (los rasgos no son independientes).
    /// Es el respaldo del modelo y la vara con la que se mide.
    /// </summary>
    public static class ReglaImpagos
    {
        public const double SUAVIZADO = 20;
        public const double ENCOGE = 0.6;
        /// <summary>Tramo de «no hay dato» (nunca ha devuelto, nivel general sin datos).</summary>
        public const int SIN_DATO = 9;

        public const string ULTIMA_DEVOLUCION = "UltimaDevolucion";
        public const string DEVOLUCIONES_12_MESES = "Devoluciones12Meses";
        public const string RECIBOS_ANTERIORES = "RecibosAnteriores";
        public const string IMPORTE = "Importe";
        public const string NIVEL_GENERAL = "NivelGeneral";
        public const string DIA_MES = "DiaMes";
        public const string RECIBOS_ULTIMOS_45 = "RecibosUltimos45";

        internal static readonly double[] CortesUltimaDevolucion = { 30, 90, 180, 365 };
        internal static readonly double[] CortesDevoluciones12Meses = { 0, 1, 2 };
        internal static readonly double[] CortesRecibosAnteriores = { 0, 2, 5, 10, 20, 50 };
        internal static readonly double[] CortesImporte = { 50, 100, 200, 400, 800 };
        /// <summary>Cuartiles y percentil 90 del nivel general de 14 días en el análisis.</summary>
        internal static readonly double[] CortesNivelGeneral = { 0.0233, 0.0310, 0.0432, 0.0623 };
        internal static readonly double[] CortesDiaMes = { 5, 10, 15, 20, 25 };
        internal static readonly double[] CortesRecibosUltimos45 = { 0, 1, 3 };

        /// <summary>Número de cortes estrictamente menores que el valor (el bisect_left del análisis): «hasta el corte» incluido.</summary>
        public static int Tramo(double valor, double[] cortes) => cortes.Count(c => c < valor);

        /// <summary>El tramo de cada rasgo de la regla.</summary>
        public static Dictionary<string, int> Tramos(RasgosImpago r)
            => new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [ULTIMA_DEVOLUCION] = r.DiasDesdeUltimaDevolucion < 0 ? SIN_DATO : Tramo(r.DiasDesdeUltimaDevolucion, CortesUltimaDevolucion),
                [DEVOLUCIONES_12_MESES] = Tramo(r.Devoluciones12Meses, CortesDevoluciones12Meses),
                [RECIBOS_ANTERIORES] = Tramo(r.RecibosAnteriores, CortesRecibosAnteriores),
                [IMPORTE] = Tramo(r.Importe, CortesImporte),
                [NIVEL_GENERAL] = r.NivelGeneral14 < 0 ? SIN_DATO : Tramo(r.NivelGeneral14, CortesNivelGeneral),
                [DIA_MES] = Tramo(r.DiaMes, CortesDiaMes),
                [RECIBOS_ULTIMOS_45] = Tramo(r.RecibosUltimos45, CortesRecibosUltimos45)
            };

        internal static string Clave(string rasgo, int tramo) => rasgo + "|" + tramo.ToString(CultureInfo.InvariantCulture);

        /// <summary>Ajusta los pesos con los ejemplos dados. Sin ejemplos o sin ninguna devolución (o todas), null.</summary>
        public static PesosReglaImpagos Ajustar(IEnumerable<EjemploImpago> ejemplos, DateTime fecha)
        {
            List<EjemploImpago> lista = (ejemplos ?? Enumerable.Empty<EjemploImpago>()).Where(e => e?.Rasgos != null).ToList();
            int devueltos = lista.Count(e => e.Devuelto);
            if (lista.Count == 0 || devueltos == 0 || devueltos == lista.Count)
            {
                return null;
            }
            double tasaBase = (double)devueltos / lista.Count;
            double logOddsBase = LogOdds(tasaBase);
            var cuenta = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (EjemploImpago e in lista)
            {
                foreach (KeyValuePair<string, int> t in Tramos(e.Rasgos))
                {
                    string clave = Clave(t.Key, t.Value);
                    if (!cuenta.TryGetValue(clave, out double[] c))
                    {
                        cuenta[clave] = c = new double[2];
                    }
                    c[0]++;
                    if (e.Devuelto)
                    {
                        c[1]++;
                    }
                }
            }
            return new PesosReglaImpagos
            {
                Fecha = fecha,
                Ejemplos = lista.Count,
                TasaBase = tasaBase,
                LogOddsBase = logOddsBase,
                Pesos = cuenta.ToDictionary(c => c.Key, c => Math.Round(LogOdds((c.Value[1] + SUAVIZADO * tasaBase) / (c.Value[0] + SUAVIZADO)) - logOddsBase, 6), StringComparer.Ordinal)
            };
        }

        /// <summary>Probabilidad de devolución (0..1). Un tramo que no se vio al ajustar no suma ni resta.</summary>
        public static double Probabilidad(PesosReglaImpagos pesos, RasgosImpago rasgos)
        {
            if (pesos == null)
            {
                throw new ArgumentNullException(nameof(pesos));
            }
            double suma = Contribuciones(pesos, rasgos).Values.Sum();
            return 1 / (1 + Math.Exp(-(pesos.LogOddsBase + ENCOGE * suma)));
        }

        /// <summary>El peso de cada rasgo de la regla para este recibo (positivo = sube el riesgo).</summary>
        public static Dictionary<string, double> Contribuciones(PesosReglaImpagos pesos, RasgosImpago rasgos)
            => Tramos(rasgos).ToDictionary(t => t.Key,
                t => pesos?.Pesos != null && pesos.Pesos.TryGetValue(Clave(t.Key, t.Value), out double peso) ? peso : 0.0, StringComparer.Ordinal);

        private static double LogOdds(double p) => Math.Log(p / (1 - p));
    }

    /// <summary>
    /// NestoAPI#896: por qué un recibo tiene riesgo, en frases comprobables (de usted, para Administración). PURA. Vale para las causas
    /// del modelo (contribución por rasgo) y para las de LA REGLA (peso por tramo): las <see cref="CAUSAS"/> que más suben el riesgo.
    /// </summary>
    public static class TextosRiesgoImpago
    {
        public const int CAUSAS = 2;
        private static readonly CultureInfo es = new CultureInfo("es-ES");

        /// <summary>Los rasgos de la regla con el nombre del rasgo del modelo que los cuenta.</summary>
        private static readonly Dictionary<string, string> DeLaRegla = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ReglaImpagos.ULTIMA_DEVOLUCION] = nameof(RasgosImpago.DiasDesdeUltimaDevolucion),
            [ReglaImpagos.DEVOLUCIONES_12_MESES] = nameof(RasgosImpago.Devoluciones12Meses),
            [ReglaImpagos.RECIBOS_ANTERIORES] = nameof(RasgosImpago.RecibosAnteriores),
            [ReglaImpagos.IMPORTE] = nameof(RasgosImpago.Importe),
            [ReglaImpagos.NIVEL_GENERAL] = nameof(RasgosImpago.NivelGeneral14),
            [ReglaImpagos.DIA_MES] = nameof(RasgosImpago.DiaMes),
            [ReglaImpagos.RECIBOS_ULTIMOS_45] = nameof(RasgosImpago.RecibosUltimos45)
        };

        /// <summary>Las frases de las causas de más contribución positiva (como mucho <paramref name="causas"/>, sin repetir frase).</summary>
        public static List<string> Motivos<T>(IEnumerable<KeyValuePair<string, T>> contribuciones, RasgosImpago rasgos, int causas = CAUSAS) where T : IConvertible
        {
            if (contribuciones == null || rasgos == null)
            {
                return new List<string>();
            }
            return contribuciones
                .Select(c => new { c.Key, Valor = c.Value.ToDouble(CultureInfo.InvariantCulture) })
                .Where(c => c.Valor > 0.0001)
                .OrderByDescending(c => c.Valor).ThenBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => Frase(DeLaRegla.TryGetValue(c.Key, out string rasgo) ? rasgo : c.Key, rasgos))
                .Where(f => !string.IsNullOrEmpty(f))
                .Distinct()
                .Take(causas)
                .ToList();
        }

        /// <summary>La frase de un rasgo con el valor del recibo; null si ese rasgo no se sabe contar (o no dice nada con ese valor).</summary>
        public static string Frase(string rasgo, RasgosImpago r)
        {
            switch (rasgo)
            {
                case nameof(RasgosImpago.DiasDesdeUltimaDevolucion):
                case nameof(RasgosImpago.MotivoUltima):
                    return r.DiasDesdeUltimaDevolucion < 0 ? null
                        : $"devolvió un recibo hace {Entero(r.DiasDesdeUltimaDevolucion)} días{MotivoEnTexto(r.MotivoUltima)}";
                case nameof(RasgosImpago.Devoluciones3Meses):
                    return r.Devoluciones3Meses <= 0 ? null : $"ha devuelto {Veces(r.Devoluciones3Meses)} en los últimos tres meses";
                case nameof(RasgosImpago.Devoluciones12Meses):
                    return r.Devoluciones12Meses <= 0 ? null : $"ha devuelto {Veces(r.Devoluciones12Meses)} en el último año";
                case nameof(RasgosImpago.DevolucionesTotales):
                case nameof(RasgosImpago.TasaDevolucionCliente):
                    return r.DevolucionesTotales <= 0 ? null : $"ha devuelto {Entero(r.DevolucionesTotales)} de sus {Entero(r.RecibosAnteriores)} recibos anteriores";
                case nameof(RasgosImpago.RecibosAnteriores):
                case nameof(RasgosImpago.Recibos12Meses):
                case nameof(RasgosImpago.AntiguedadDias):
                    return r.RecibosAnteriores <= 0 ? "es su primer recibo"
                        : r.RecibosAnteriores <= 5 ? $"solo se le han girado {Entero(r.RecibosAnteriores)} recibos antes" : null;
                case nameof(RasgosImpago.RecibosUltimos45):
                    return r.RecibosUltimos45 <= 0 ? null : $"tiene {Entero(r.RecibosUltimos45)} recibos girados en los últimos 45 días";
                case nameof(RasgosImpago.Importe):
                    return $"el importe es de {r.Importe.ToString("N2", es)} €";
                case nameof(RasgosImpago.RelacionImporteHabitual):
                    return r.RelacionImporteHabitual >= 1.5f ? $"el importe es {r.RelacionImporteHabitual.ToString("0.#", es)} veces el habitual de sus recibos" : null;
                case nameof(RasgosImpago.EsRegiro):
                    return r.EsRegiro > 0 ? "este mismo recibo ya se devolvió antes" : null;
                case nameof(RasgosImpago.PrimerGiroCuenta):
                    return r.PrimerGiroCuenta > 0 && r.RecibosAnteriores > 0 ? "es el primer giro a esa cuenta" : null;
                case nameof(RasgosImpago.NivelGeneral14):
                case nameof(RasgosImpago.NivelGeneral30):
                    return r.NivelGeneral14 < 0 ? null : $"estos días se están devolviendo más recibos de lo normal (el {(r.NivelGeneral14 * 100).ToString("0.#", es)} %)";
                case nameof(RasgosImpago.DiaMes):
                    return r.DiaMes > 25 || r.DiaMes <= 5 ? "se gira a final o principio de mes" : null;
                default:
                    return null;
            }
        }

        private static string MotivoEnTexto(string motivo)
        {
            switch (motivo)
            {
                case MotivosImpago.SALDO: return " (saldo insuficiente)";
                case MotivosImpago.MANDATO: return " (mandato no válido)";
                case MotivosImpago.CUENTA: return " (cuenta bloqueada o cancelada)";
                case MotivosImpago.DISCONFORME: return " (disconforme)";
                default: return string.Empty;
            }
        }

        private static string Veces(float n) => n == 1 ? "1 vez" : $"{Entero(n)} veces";

        private static string Entero(float n) => Math.Round(n).ToString("0", es);
    }
}
