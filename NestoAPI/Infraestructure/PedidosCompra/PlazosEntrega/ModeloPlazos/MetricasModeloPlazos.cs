using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega.ModeloPlazos
{
    /// <summary>NestoAPI#853: cómo acierta una forma de prever el plazo (regla o modelo) en un conjunto de pedidos.</summary>
    public class MetricasPlazos
    {
        public int Pedidos { get; set; }
        /// <summary>Media de |real − previsto| en días.</summary>
        public double ErrorMedio { get; set; }
        /// <summary>% de pedidos que llegaron DESPUÉS de lo previsto (se quedó corto), de 0 a 100.</summary>
        public double PorcentajeCorto { get; set; }
        /// <summary>Cuando se pasa de largo (llegó antes de lo previsto), días de más de media. Null si nunca se pasa.</summary>
        public double? DiasDeMasMedio { get; set; }

        /// <summary>PURA. Pares (días reales, días previstos).</summary>
        public static MetricasPlazos Calcular(IEnumerable<(int Real, int Previsto)> pares)
        {
            List<(int Real, int Previsto)> lista = (pares ?? Enumerable.Empty<(int, int)>()).ToList();
            if (lista.Count == 0)
            {
                return new MetricasPlazos();
            }
            List<int> deMas = lista.Where(p => p.Previsto > p.Real).Select(p => p.Previsto - p.Real).ToList();
            return new MetricasPlazos
            {
                Pedidos = lista.Count,
                ErrorMedio = Math.Round(lista.Average(p => (double)Math.Abs(p.Real - p.Previsto)), 2),
                PorcentajeCorto = Math.Round(100.0 * lista.Count(p => p.Real > p.Previsto) / lista.Count, 2),
                DiasDeMasMedio = deMas.Any() ? Math.Round(deMas.Average(), 2) : (double?)null
            };
        }
    }

    public class DecisionPuertaPlazos
    {
        public bool Promover { get; set; }
        public string Motivo { get; set; }
    }

    /// <summary>
    /// NestoAPI#853: puerta de calidad del modelo de plazos. PURA. El nuevo solo sustituye al guardado si no empeora en validación:
    /// % de veces que se queda corto (lo que más importa: la fecha es prudente a propósito) como mucho <see cref="TOLERANCIA_CORTO"/>
    /// puntos peor y error medio como mucho <see cref="TOLERANCIA_ERROR"/> días peor (las ventanas de validación de dos noches
    /// seguidas no son idénticas: esa holgura es su ruido, no un permiso para empeorar). Sin modelo guardado, pasa el primero.
    /// </summary>
    public static class PuertaCalidadPlazos
    {
        public const int MINIMO_VALIDACION = 30;
        public const double TOLERANCIA_CORTO = 1.0;
        public const double TOLERANCIA_ERROR = 0.25;

        public static DecisionPuertaPlazos Decidir(MetricasPlazos activo, bool hayModeloGuardado, MetricasPlazos nuevo)
        {
            if (nuevo == null || nuevo.Pedidos < MINIMO_VALIDACION)
            {
                return new DecisionPuertaPlazos { Promover = false, Motivo = $"pocos pedidos en validación ({nuevo?.Pedidos ?? 0}; hacen falta {MINIMO_VALIDACION})" };
            }
            if (!hayModeloGuardado || activo == null || activo.Pedidos == 0)
            {
                return new DecisionPuertaPlazos { Promover = true, Motivo = "no había modelo guardado: " + Texto(nuevo) };
            }
            var fallos = new List<string>();
            if (nuevo.PorcentajeCorto > activo.PorcentajeCorto + TOLERANCIA_CORTO + 1e-9)
            {
                fallos.Add($"se queda corto más veces ({Num(activo.PorcentajeCorto)} % → {Num(nuevo.PorcentajeCorto)} %)");
            }
            if (nuevo.ErrorMedio > activo.ErrorMedio + TOLERANCIA_ERROR + 1e-9)
            {
                fallos.Add($"más error medio ({Num(activo.ErrorMedio)} → {Num(nuevo.ErrorMedio)} días)");
            }
            return fallos.Any()
                ? new DecisionPuertaPlazos { Promover = false, Motivo = "empeora: " + string.Join(" y ", fallos) }
                : new DecisionPuertaPlazos { Promover = true, Motivo = $"no empeora (corto {Num(activo.PorcentajeCorto)} % → {Num(nuevo.PorcentajeCorto)} %, error {Num(activo.ErrorMedio)} → {Num(nuevo.ErrorMedio)} días)" };
        }

        internal static string Texto(MetricasPlazos m) => $"corto {Num(m.PorcentajeCorto)} %, error medio {Num(m.ErrorMedio)} días";

        internal static string Num(double v) => v.ToString("0.##", CultureInfo.GetCultureInfo("es-ES"));
    }

    /// <summary>Un envío ya recibido con las dos fechas (de dbo.vPedidosCompraEnviosError).</summary>
    public class EnvioRecibidoPlazos
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public DateTime FechaEnvio { get; set; }
        public DateTime FechaRegla { get; set; }
        public DateTime FechaModelo { get; set; }
        public DateTime PrimerAlbaran { get; set; }
    }

    /// <summary>Regla contra modelo con lo que ha pasado de verdad.</summary>
    public class ComparacionRealPlazos
    {
        public int Pedidos { get; set; }
        public MetricasPlazos Regla { get; set; }
        public MetricasPlazos Modelo { get; set; }
        public string Veredicto { get; set; }
    }

    /// <summary>NestoAPI#853: la comparación real y su veredicto. PURA.</summary>
    public static class ComparacionPlazos
    {
        public const int MINIMO_PEDIDOS = 50;
        public const string REGLA = "la regla del P80";
        public const string MODELO = "el modelo";

        public static ComparacionRealPlazos Comparar(IEnumerable<EnvioRecibidoPlazos> envios)
        {
            List<EnvioRecibidoPlazos> lista = (envios ?? Enumerable.Empty<EnvioRecibidoPlazos>()).Where(e => e != null).ToList();
            MetricasPlazos regla = MetricasPlazos.Calcular(lista.Select(e => (Real: Dias(e.FechaEnvio, e.PrimerAlbaran), Previsto: Dias(e.FechaEnvio, e.FechaRegla))));
            MetricasPlazos modelo = MetricasPlazos.Calcular(lista.Select(e => (Real: Dias(e.FechaEnvio, e.PrimerAlbaran), Previsto: Dias(e.FechaEnvio, e.FechaModelo))));
            return new ComparacionRealPlazos { Pedidos = lista.Count, Regla = regla, Modelo = modelo, Veredicto = Veredicto(lista.Count, regla, modelo) };
        }

        public static string Veredicto(int pedidos, MetricasPlazos regla, MetricasPlazos modelo)
        {
            if (pedidos < MINIMO_PEDIDOS || regla == null || modelo == null)
            {
                return $"Aún no hay datos suficientes: {pedidos} pedidos recibidos con las dos fechas (hacen falta {MINIMO_PEDIDOS}).";
            }
            string corto = Ganador(regla.PorcentajeCorto, modelo.PorcentajeCorto);
            string error = Ganador(regla.ErrorMedio, modelo.ErrorMedio);
            string textoCorto = corto == null
                ? $"Se quedan cortos las mismas veces ({PuertaCalidadPlazos.Num(regla.PorcentajeCorto)} %)."
                : $"Se queda corto menos veces {corto} ({PuertaCalidadPlazos.Num(corto == MODELO ? modelo.PorcentajeCorto : regla.PorcentajeCorto)} % frente a " +
                  $"{PuertaCalidadPlazos.Num(corto == MODELO ? regla.PorcentajeCorto : modelo.PorcentajeCorto)} %).";
            string textoError = error == null
                ? $"Mismo error medio ({PuertaCalidadPlazos.Num(regla.ErrorMedio)} días)."
                : $"Menos error medio {error} ({PuertaCalidadPlazos.Num(error == MODELO ? modelo.ErrorMedio : regla.ErrorMedio)} días frente a " +
                  $"{PuertaCalidadPlazos.Num(error == MODELO ? regla.ErrorMedio : modelo.ErrorMedio)}).";
            return $"{textoCorto} {textoError} Medido con {pedidos} pedidos recibidos.";
        }

        /// <summary>El que tiene el valor más bajo; null si empatan.</summary>
        private static string Ganador(double regla, double modelo)
            => Math.Abs(regla - modelo) < 1e-9 ? null : (modelo < regla ? MODELO : REGLA);

        private static int Dias(DateTime desde, DateTime hasta) => (hasta.Date - desde.Date).Days;
    }
}
