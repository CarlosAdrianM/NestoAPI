using ModeloLlamadaPedido.Entrenamiento;
using ModeloLlamadaPedido.Evaluacion;
using ModeloLlamadaPedido.Features;

namespace ModeloLlamadaPedido.Reentrenamiento;

/// <summary>
/// NestoAPI#619: qué se entrena y contra qué se compara. Lo rellenan la consola (argumentos) y el job de la API (appSettings
/// con valores por defecto en código). Con `set` y no `init`: la API compila con C# 7.3 y no sabe asignar `init`.
/// </summary>
public sealed class OpcionesReentrenamiento
{
    /// <summary>3 años, todos los vendedores. Si es false, muestra de <see cref="Vendedor"/> con <see cref="Meses"/> meses.</summary>
    public bool Completo { get; set; } = true;
    public string? Vendedor { get; set; }
    public int Meses { get; set; }
    public int MesesValidacion { get; set; } = 6;
    public int K { get; set; } = 20;
    public string Trainer { get; set; } = Entrenador.LIGHTGBM;
    /// <summary>Pesos de clase (positivos × negativos/positivos); descalibra la probabilidad.</summary>
    public bool Pesos { get; set; }
    /// <summary>Evaluar también el otro trainer y las bases (solo informativo, para el README). La API no lo necesita.</summary>
    public bool EvaluarAlternativas { get; set; } = true;
    /// <summary>Zip del modelo de producción con el que se compara. Null o inexistente = no hay con qué comparar (no se promueve).</summary>
    public string? ModeloActual { get; set; }
    /// <summary>Primer día de contactos que el modelo actual NO vio al entrenar (ver <see cref="VentanaReentrenamiento.CorteDesdeFechaModelo"/>).</summary>
    public DateTime? CorteActual { get; set; }
    /// <summary>Fracción: 0,01 = 1 punto de precision@k.</summary>
    public double MaxBajadaPrecision { get; set; } = PuertaCalidad.MAX_BAJADA_PRECISION_POR_DEFECTO;
    public double MaxBajadaAuc { get; set; } = PuertaCalidad.MAX_BAJADA_AUC_POR_DEFECTO;

    public string Alcance => Completo ? "COMPLETO (todos los vendedores)" : $"MUESTRA {Vendedor}, {Meses} meses";
}

public static class VentanaReentrenamiento
{
    /// <summary>Ventana de contactos: termina 8 días antes de hoy (etiqueta de 7 días cerrada); 3 años o N meses.</summary>
    public static (DateTime desde, DateTime hasta, DateTime corte) Calcular(DateTime hoy, bool completo, int meses, int mesesValidacion)
    {
        DateTime hasta = hoy.Date.AddDays(-(CalculadoraFeatures.VENTANA_POSITIVO_DIAS + 1));
        DateTime desde = completo ? hasta.AddYears(-3) : hasta.AddMonths(-meses);
        int validacion = completo ? mesesValidacion : Math.Min(mesesValidacion, Math.Max(1, meses / 2));
        return (desde, hasta, hasta.AddMonths(-validacion));
    }

    /// <summary>
    /// Primer día de contactos que un modelo entrenado (o promovido) el día <paramref name="fechaModelo"/> no vio: esa fecha
    /// menos 8 días (la ventana del entrenamiento termina 8 días antes de entrenar). Si la fecha real de entrenamiento es
    /// anterior, el corte sale más tarde de lo necesario: menos filas en la comparación, pero ninguna vista.
    /// </summary>
    public static DateTime CorteDesdeFechaModelo(DateTime fechaModelo)
        => fechaModelo.Date.AddDays(-(CalculadoraFeatures.VENTANA_POSITIVO_DIAS + 1));
}
