namespace ModeloLlamadaPedido.Evaluacion;

/// <summary>Las dos métricas que mira la puerta, medidas sobre el mismo conjunto de validación temporal.</summary>
public sealed record MetricasPuerta(double PrecisionK, double Auc);

public sealed record DecisionPuerta(bool Promover, string Motivo);

/// <summary>
/// Puerta de calidad del reentrenamiento mensual (NestoAPI#619): el modelo nuevo solo sustituye al de producción si su
/// precision@k media no baja más de <c>maxBajadaPrecision</c> (fracción: 0,01 = 1 punto) y su AUC no baja más de
/// <c>maxBajadaAuc</c> (0 = no puede bajar). Sin modelo actual o con métricas que no se pueden calcular, no se promueve:
/// la puerta nunca deja pasar un modelo a ciegas. Función pura (sin E/S), probada en PuertaCalidadTests.
/// </summary>
public static class PuertaCalidad
{
    public const double MAX_BAJADA_PRECISION_POR_DEFECTO = 0.01;
    public const double MAX_BAJADA_AUC_POR_DEFECTO = 0.0;
    private const double EPSILON = 1e-9;

    public static DecisionPuerta Decidir(MetricasPuerta? actual, MetricasPuerta nuevo, int k,
        double maxBajadaPrecision = MAX_BAJADA_PRECISION_POR_DEFECTO, double maxBajadaAuc = MAX_BAJADA_AUC_POR_DEFECTO)
    {
        if (double.IsNaN(nuevo.PrecisionK) || double.IsNaN(nuevo.Auc))
            return new DecisionPuerta(false, $"no se puede calcular la P@{k} o el AUC del modelo nuevo (pocos contactos en la comparación)");
        if (actual == null)
            return new DecisionPuerta(false, "no hay modelo actual con el que comparar");
        if (double.IsNaN(actual.PrecisionK) || double.IsNaN(actual.Auc))
            return new DecisionPuerta(false, $"no se puede calcular la P@{k} o el AUC del modelo actual (pocos contactos en la comparación)");

        string comparacion = Comparacion(actual, nuevo, k);
        var fallos = new List<string>();
        if (nuevo.PrecisionK < actual.PrecisionK - maxBajadaPrecision - EPSILON)
            fallos.Add($"la P@{k} baja {Informe.Num((actual.PrecisionK - nuevo.PrecisionK) * 100, "0.0")} puntos " +
                       $"(máximo permitido: {Informe.Num(maxBajadaPrecision * 100, "0.0")})");
        if (nuevo.Auc < actual.Auc - maxBajadaAuc - EPSILON)
            fallos.Add(maxBajadaAuc <= 0
                ? "el AUC baja"
                : $"el AUC baja {Informe.Num(actual.Auc - nuevo.Auc, "0.000")} (máximo permitido: {Informe.Num(maxBajadaAuc, "0.000")})");

        return fallos.Count == 0
            ? new DecisionPuerta(true, comparacion)
            : new DecisionPuerta(false, $"{string.Join(" y ", fallos)}; {comparacion}");
    }

    /// <summary>«P@20 62,4 % → 63,0 %, AUC 0,792 → 0,795».</summary>
    public static string Comparacion(MetricasPuerta? actual, MetricasPuerta nuevo, int k)
        => $"P@{k} {(actual == null ? "—" : Informe.Pct(actual.PrecisionK))} → {Informe.Pct(nuevo.PrecisionK)}, " +
           $"AUC {(actual == null ? "—" : Informe.Num(actual.Auc, "0.000"))} → {Informe.Num(nuevo.Auc, "0.000")}";
}
