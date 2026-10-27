using System.Globalization;

namespace ModeloSugerenciasProductos.Aceptacion;

/// <summary>NestoAPI#671 (corte 2e): qué se entrena y cómo se decide. Los valores por defecto son los del plan del 20/10/26.</summary>
public sealed class OpcionesAceptacion
{
    // ---- Umbral de datos (plan del 20/10/26): sin esto ni se entrena ----
    public int MinimoRespondidas { get; set; } = 1000;
    public int MinimoAnadidos { get; set; } = 150;
    public int MinimoVendedores { get; set; } = 3;

    // ---- Partición temporal ----
    /// <summary>Las últimas sesiones (por fecha), con sus respuestas, que se guardan para validar.</summary>
    public double FraccionValidacion { get; set; } = 0.25;

    // ---- Puerta ----
    /// <summary>Respuestas (con P1) en la validación, como poco, para poder decidir.</summary>
    public int MinimoFilasValidacion { get; set; } = 200;
    public int MinimoPositivosValidacion { get; set; } = 30;
    /// <summary>El AUC del modelo 2 tiene que ganar al de P1 (el orden de hoy) por al menos esto.</summary>
    public double MinimaMejoraAuc { get; set; } = 0.02;
    /// <summary>Sesiones de validación con ≥ 2 respuestas y ≥ 1 «Añadido» para que cuente la precisión@1.</summary>
    public int MinimoSesionesPrecision { get; set; } = 20;

    // ---- Datos ----
    /// <summary>Peso de las señales débiles (sin tocar, pero entró en el pedido) en el entrenamiento; 0 = no se usan. Nunca se validan.</summary>
    public float PesoSenalDebil { get; set; } = 0.25f;

    // ---- LightGBM (pocos datos: árboles pequeños) ----
    public int Hojas { get; set; } = 15;
    public int Iteraciones { get; set; } = 200;
    public int MinimoEjemplosPorHoja { get; set; } = 20;
    public double TasaAprendizaje { get; set; } = 0.05;
    public int Hilos { get; set; } = 1;
}

/// <summary>NestoAPI#671 (corte 2e): ¿hay respuestas suficientes para entrenar el modelo 2?</summary>
public static class UmbralDatosAceptacion
{
    public sealed class Resultado
    {
        public bool Suficientes { get; set; }
        public int Respondidas { get; set; }
        public int Anadidos { get; set; }
        public int Vendedores { get; set; }
        public string Motivo { get; set; } = "";
    }

    public static Resultado Comprobar(IEnumerable<MostradaAceptacion> mostradas, OpcionesAceptacion op)
    {
        op ??= new OpcionesAceptacion();
        List<MostradaAceptacion> respondidas = (mostradas ?? Enumerable.Empty<MostradaAceptacion>()).Where(m => m != null && m.EsRespondida).ToList();
        int anadidos = respondidas.Count(m => m.Aceptada);
        int vendedores = respondidas.Select(m => m.Vendedor?.Trim()).Where(v => !string.IsNullOrEmpty(v))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var faltan = new List<string>();
        if (respondidas.Count < op.MinimoRespondidas) faltan.Add($"{respondidas.Count} respuestas de {op.MinimoRespondidas}");
        if (anadidos < op.MinimoAnadidos) faltan.Add($"{anadidos} «Añadido» de {op.MinimoAnadidos}");
        if (vendedores < op.MinimoVendedores) faltan.Add($"{vendedores} vendedores de {op.MinimoVendedores}");
        return new Resultado
        {
            Suficientes = faltan.Count == 0,
            Respondidas = respondidas.Count,
            Anadidos = anadidos,
            Vendedores = vendedores,
            Motivo = faltan.Count == 0
                ? $"{respondidas.Count} respuestas, {anadidos} «Añadido» y {vendedores} vendedores"
                : "faltan datos: " + string.Join(", ", faltan)
        };
    }
}

/// <summary>Las métricas de una forma de ordenar en la validación.</summary>
public sealed class ResultadoAceptacion
{
    public string Nombre { get; set; } = "";
    public int Filas { get; set; }
    public int Positivos { get; set; }
    /// <summary>Área bajo la curva ROC: probabilidad de que un «Añadido» puntúe más que un «No le interesa». NaN si falta alguna clase.</summary>
    public double Auc { get; set; } = double.NaN;
    /// <summary>Sesiones con ≥ 2 respuestas y ≥ 1 «Añadido».</summary>
    public int SesionesPrecision { get; set; }
    /// <summary>En esas sesiones, la proporción en que la mejor puntuada fue un «Añadido» (precisión@1).</summary>
    public double PrecisionPrimera { get; set; } = double.NaN;
}

public sealed class DecisionAceptacion
{
    public bool Pasa { get; set; }
    public string Motivo { get; set; } = "";
}

/// <summary>NestoAPI#671 (corte 2e): métricas fuera de muestra del modelo 2 (y de lo que hay hoy) y la puerta.</summary>
public static class MetricasAceptacion
{
    /// <summary>AUC por rangos (Mann-Whitney), con los empates a medias. NaN si no hay positivos o negativos.</summary>
    public static double Auc(IEnumerable<(double puntuacion, bool etiqueta)> filas)
    {
        var lista = (filas ?? Enumerable.Empty<(double, bool)>()).Where(f => !double.IsNaN(f.puntuacion)).OrderBy(f => f.puntuacion).ToList();
        long positivos = lista.LongCount(f => f.etiqueta);
        long negativos = lista.Count - positivos;
        if (positivos == 0 || negativos == 0) return double.NaN;
        double sumaRangos = 0;
        int i = 0;
        while (i < lista.Count)
        {
            int j = i;
            while (j + 1 < lista.Count && lista[j + 1].puntuacion == lista[i].puntuacion) j++;
            double rangoMedio = (i + j + 2) / 2.0; // rangos 1-based
            for (int k = i; k <= j; k++)
            {
                if (lista[k].etiqueta) sumaRangos += rangoMedio;
            }
            i = j + 1;
        }
        return (sumaRangos - positivos * (positivos + 1) / 2.0) / (positivos * (double)negativos);
    }

    /// <summary>
    /// Precisión@1 por sesión: en las sesiones con ≥ 2 respuestas y ≥ 1 «Añadido», si la mejor puntuada es un «Añadido».
    /// Empate: la de menor posición (la que se enseñó antes). Devuelve (sesiones, precisión; NaN si no hay).
    /// </summary>
    public static (int sesiones, double precision) PrecisionPrimera(IEnumerable<FilaAceptacion> filas, Func<FilaAceptacion, double> puntuacion)
    {
        int sesiones = 0, aciertos = 0;
        foreach (IGrouping<string, FilaAceptacion> g in (filas ?? Enumerable.Empty<FilaAceptacion>()).GroupBy(f => f.Sesion, StringComparer.Ordinal))
        {
            List<FilaAceptacion> filasSesion = g.ToList();
            if (filasSesion.Count < 2 || !filasSesion.Any(f => f.Label)) continue;
            sesiones++;
            FilaAceptacion mejor = filasSesion.OrderByDescending(puntuacion).ThenBy(f => f.Posicion).First();
            if (mejor.Label) aciertos++;
        }
        return (sesiones, sesiones == 0 ? double.NaN : aciertos / (double)sesiones);
    }

    public static ResultadoAceptacion Evaluar(string nombre, IReadOnlyList<FilaAceptacion> filas, Func<FilaAceptacion, double> puntuacion)
    {
        (int sesiones, double precision) = PrecisionPrimera(filas, puntuacion);
        return new ResultadoAceptacion
        {
            Nombre = nombre,
            Filas = filas.Count,
            Positivos = filas.Count(f => f.Label),
            Auc = Auc(filas.Select(f => (puntuacion(f), f.Label))),
            SesionesPrecision = sesiones,
            PrecisionPrimera = precision
        };
    }

    /// <summary>
    /// La puerta: (1) bastantes respuestas en la validación; (2) AUC del modelo ≥ AUC de P1 (el orden de hoy) +
    /// <see cref="OpcionesAceptacion.MinimaMejoraAuc"/>; (3) no peor que el reordenador bayesiano; (4) si hay sesiones
    /// suficientes, precisión@1 no peor que la de P1.
    /// </summary>
    public static DecisionAceptacion Decidir(ResultadoAceptacion? modelo, ResultadoAceptacion? actual, ResultadoAceptacion? bayes, OpcionesAceptacion op)
    {
        op ??= new OpcionesAceptacion();
        if (modelo == null || actual == null) return new DecisionAceptacion { Motivo = "no hay validación" };
        if (modelo.Filas < op.MinimoFilasValidacion || modelo.Positivos < op.MinimoPositivosValidacion)
        {
            return new DecisionAceptacion { Motivo = $"validación demasiado pequeña ({modelo.Filas} respuestas con P1 y {modelo.Positivos} «Añadido»; hacen falta {op.MinimoFilasValidacion} y {op.MinimoPositivosValidacion})" };
        }
        if (double.IsNaN(modelo.Auc) || double.IsNaN(actual.Auc))
        {
            return new DecisionAceptacion { Motivo = "no se puede calcular el AUC (falta alguna clase en la validación)" };
        }
        string auc = $"AUC {F(modelo.Auc)} frente a {F(actual.Auc)} de P1";
        if (modelo.Auc < actual.Auc + op.MinimaMejoraAuc)
        {
            return new DecisionAceptacion { Motivo = $"{auc}: no gana por {F(op.MinimaMejoraAuc)}" };
        }
        if (bayes != null && !double.IsNaN(bayes.Auc) && modelo.Auc < bayes.Auc)
        {
            return new DecisionAceptacion { Motivo = $"{auc}, pero el reordenador bayesiano da {F(bayes.Auc)}" };
        }
        if (modelo.SesionesPrecision >= op.MinimoSesionesPrecision && !double.IsNaN(actual.PrecisionPrimera)
            && modelo.PrecisionPrimera < actual.PrecisionPrimera)
        {
            return new DecisionAceptacion { Motivo = $"{auc}, pero la precisión@1 baja ({P(modelo.PrecisionPrimera)} frente a {P(actual.PrecisionPrimera)} en {modelo.SesionesPrecision} sesiones)" };
        }
        return new DecisionAceptacion { Pasa = true, Motivo = $"{auc} (mejora mínima {F(op.MinimaMejoraAuc)})" };
    }

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    internal static string F(double v) => double.IsNaN(v) ? "—" : v.ToString("0.000", Es);
    internal static string P(double v) => double.IsNaN(v) ? "—" : (v * 100).ToString("0.0", Es) + " %";
}
