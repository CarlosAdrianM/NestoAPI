using System.Globalization;
using System.Text;
using ModeloSugerenciasProductos.Features;

namespace ModeloSugerenciasProductos.Evaluacion;

/// <summary>
/// La diversidad de las 3 que se ven (plan del 20/10/26): la mejor entra siempre; después, como mucho 1 «habitual» (≥ 3
/// compras en 24 meses) y 1 por familia (marca). Si no hay con qué llenar, se relaja: entran las siguientes mejores, sin reglas.
/// </summary>
public static class Diversidad
{
    /// <summary>
    /// NestoAPI#671/#683: ÚNICA fuente de verdad de si se aplica la diversidad al orden del modelo. La usan Calcular
    /// (MotorSugerenciasProductos.APLICAR_DIVERSIDAD) y la evaluación del entrenamiento (la puerta mide el top-3 tal y como lo
    /// ve el vendedor). APAGADA desde el informe del 20/10/26 (P@3 26,1 % sin ella frente a 17,2 % con ella). static readonly y
    /// no const para que el código apagado siga compilándose sin avisos.
    /// </summary>
    public static readonly bool APLICAR = false;

    public const int VISIBLES = 3;
    public const int MAXIMO_HABITUALES = 1;
    public const int MAXIMO_POR_FAMILIA = 1;

    /// <param name="ordenados">Los candidatos de mejor a peor.</param>
    public static List<T> Aplicar<T>(IEnumerable<T> ordenados, Func<T, bool> habitual, Func<T, string?> familia, int k = VISIBLES)
    {
        List<T> lista = ordenados.ToList();
        var elegidos = new List<T>();
        var usados = new HashSet<int>();
        int habituales = 0;
        var familias = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < lista.Count && elegidos.Count < k; i++)
        {
            T c = lista[i];
            string f = familia(c)?.Trim() ?? "";
            bool esHabitual = habitual(c);
            if (elegidos.Count > 0)
            {
                if (esHabitual && habituales >= MAXIMO_HABITUALES) continue;
                if (f.Length > 0 && familias.TryGetValue(f, out int n) && n >= MAXIMO_POR_FAMILIA) continue;
            }
            elegidos.Add(c);
            usados.Add(i);
            if (esHabitual) habituales++;
            if (f.Length > 0) familias[f] = (familias.TryGetValue(f, out int m) ? m : 0) + 1;
        }
        for (int i = 0; i < lista.Count && elegidos.Count < k; i++)
        {
            if (usados.Add(i)) elegidos.Add(lista[i]);
        }
        return elegidos;
    }

    /// <summary>
    /// NestoAPI#683: el top-3 de una sesión tal y como lo ve el vendedor: con la diversidad si <see cref="APLICAR"/>; si no, las
    /// mejores por <paramref name="puntuacion"/> (empates por producto).
    /// </summary>
    public static List<string> TopComoLaApi(IEnumerable<FilaEntrenamiento> filas, Func<FilaEntrenamiento, double> puntuacion, int k = VISIBLES)
        => APLICAR ? Top(filas, puntuacion, k) : SinDiversidad(filas, puntuacion, k);

    /// <summary>NestoAPI#683: el top-k sin la diversidad (las mejores; empates por producto).</summary>
    public static List<string> SinDiversidad(IEnumerable<FilaEntrenamiento> filas, Func<FilaEntrenamiento, double> puntuacion, int k = VISIBLES)
        => filas.OrderByDescending(puntuacion).ThenBy(f => f.Producto, StringComparer.OrdinalIgnoreCase).Take(k).Select(f => f.Producto).ToList();

    /// <summary>Las filas de una sesión ordenadas por <paramref name="puntuacion"/> (empates por producto), con la diversidad.</summary>
    public static List<string> Top(IEnumerable<FilaEntrenamiento> filas, Func<FilaEntrenamiento, double> puntuacion, int k = VISIBLES)
        => Aplicar(filas.OrderByDescending(puntuacion).ThenBy(f => f.Producto, StringComparer.OrdinalIgnoreCase),
                f => f.EsHabitual > 0, f => f.Familia, k)
            .Select(f => f.Producto).ToList();
}

/// <summary>Precisión y recuperación de un tipo de producto (habituales, ocasionales o nuevos para el cliente).</summary>
public sealed class ResultadoSegmento
{
    public int Sugeridos { get; set; }
    public int Aciertos { get; set; }
    public int Positivos { get; set; }
    public int PositivosAcertados { get; set; }
    public double Precision => Sugeridos == 0 ? double.NaN : (double)Aciertos / Sugeridos;
    public double Recuperacion => Positivos == 0 ? double.NaN : (double)PositivosAcertados / Positivos;
}

public sealed class ResultadoRanking
{
    public string Modelo { get; set; } = "";
    /// <summary>Sesiones con al menos un positivo (las que cuentan).</summary>
    public int Sesiones { get; set; }
    public double P3 { get; set; } = double.NaN;
    public double R3 { get; set; } = double.NaN;
    public double Ndcg3 { get; set; } = double.NaN;
    public double AlMenos1 { get; set; } = double.NaN;
    /// <summary>Positivos que están entre los candidatos (lo máximo que se puede acertar).</summary>
    public double Cobertura { get; set; } = double.NaN;
    public Dictionary<string, ResultadoSegmento> PorSegmento { get; } = new();
}

/// <summary>
/// NestoAPI#671: métricas sobre las 3 que se enseñan (top-3 ya con la diversidad, salvo reglas-1, que se mide con su propia
/// mezcla como funciona en producción), por sesión y en media. Solo cuentan las sesiones con algún positivo.
/// <list type="bullet">
/// <item>P@3 = aciertos ÷ 3; R@3 = aciertos ÷ positivos; NDCG@3 binaria; «al menos 1» = la sesión tiene algún acierto.</item>
/// <item>Por segmento (para el cliente): habituales (≥ 3 compras en 24 meses), ocasionales (1-2) y nuevos (nunca en 24 meses).</item>
/// </list>
/// </summary>
public static class Metricas
{
    public const int K = 3;
    public const string HABITUALES = "Habituales";
    public const string OCASIONALES = "Ocasionales";
    public const string NUEVOS = "Nuevos";
    public static readonly string[] SEGMENTOS = { HABITUALES, OCASIONALES, NUEVOS };

    public static string Segmento(int compras24Meses)
        => compras24Meses >= Reglas.ReglasSugerencias.COMPRAS_HABITUAL ? HABITUALES : compras24Meses > 0 ? OCASIONALES : NUEVOS;

    public static ResultadoRanking Evaluar(string modelo, IEnumerable<Sesion> sesiones, Func<Sesion, IReadOnlyList<string>> top)
    {
        var r = new ResultadoRanking { Modelo = modelo };
        foreach (string s in SEGMENTOS) r.PorSegmento[s] = new ResultadoSegmento();
        double p = 0, rec = 0, ndcg = 0, alMenos = 0;
        long positivos = 0, cubiertos = 0;
        foreach (Sesion sesion in sesiones.Where(s => s.Positivos.Count > 0))
        {
            IReadOnlyList<string> lista = top(sesion);
            List<string> k = lista.Take(K).ToList();
            int aciertos = k.Count(sesion.Positivos.Contains);
            r.Sesiones++;
            p += aciertos / (double)K;
            rec += aciertos / (double)sesion.Positivos.Count;
            alMenos += aciertos > 0 ? 1 : 0;
            double dcg = 0, idcg = 0;
            for (int i = 0; i < k.Count; i++) if (sesion.Positivos.Contains(k[i])) dcg += 1 / Math.Log(i + 2, 2);
            for (int i = 0; i < Math.Min(K, sesion.Positivos.Count); i++) idcg += 1 / Math.Log(i + 2, 2);
            ndcg += dcg / idcg;
            var candidatos = new HashSet<string>(sesion.Filas.Select(f => f.Producto), StringComparer.OrdinalIgnoreCase);
            positivos += sesion.Positivos.Count;
            cubiertos += sesion.Positivos.Count(candidatos.Contains);

            foreach (string producto in k)
            {
                ResultadoSegmento seg = r.PorSegmento[Segmento(Compras(sesion, producto))];
                seg.Sugeridos++;
                if (sesion.Positivos.Contains(producto)) seg.Aciertos++;
            }
            foreach (string producto in sesion.Positivos)
            {
                ResultadoSegmento seg = r.PorSegmento[Segmento(Compras(sesion, producto))];
                seg.Positivos++;
                if (k.Contains(producto, StringComparer.OrdinalIgnoreCase)) seg.PositivosAcertados++;
            }
        }
        if (r.Sesiones > 0)
        {
            r.P3 = p / r.Sesiones;
            r.R3 = rec / r.Sesiones;
            r.Ndcg3 = ndcg / r.Sesiones;
            r.AlMenos1 = alMenos / r.Sesiones;
            r.Cobertura = positivos == 0 ? double.NaN : (double)cubiertos / positivos;
        }
        return r;
    }

    private static int Compras(Sesion s, string producto) => s.ComprasPorProducto.TryGetValue(producto, out int n) ? n : 0;
}

public sealed record DecisionPuerta(bool Pasa, string Motivo);

/// <summary>
/// Puerta de salida del modelo 1 (plan del 20/10/26): ganar a reglas-1 en P@3 por al menos 2 puntos, con la NDCG@3 no peor.
/// NestoAPI#683: y no perder más de <c>maximaPerdidaFrenteAnterior</c> de P@3 frente al mismo modelo con las features de la
/// versión anterior entrenado con la misma partición (si no, la API seguiría con el zip anterior, que es mejor). Sin métricas
/// calculables, no pasa. Función pura.
/// </summary>
public static class PuertaCalidad
{
    private const double EPSILON = 1e-9;
    /// <summary>NestoAPI#683: lo que se tolera perder de P@3 frente al modelo de la versión anterior (0,005 = medio punto).</summary>
    public const double MAXIMA_PERDIDA_FRENTE_ANTERIOR = 0.005;

    public static DecisionPuerta Decidir(ResultadoRanking modelo, ResultadoRanking reglas, double minimaMejoraP3 = 0.02,
        ResultadoRanking? versionAnterior = null, double maximaPerdidaFrenteAnterior = MAXIMA_PERDIDA_FRENTE_ANTERIOR)
    {
        if (double.IsNaN(modelo.P3) || double.IsNaN(modelo.Ndcg3) || double.IsNaN(reglas.P3) || double.IsNaN(reglas.Ndcg3))
            return new DecisionPuerta(false, "no se pueden calcular las métricas (no hay sesiones de validación con positivos)");
        string comparacion = $"P@3 {Informe.Pct(reglas.P3)} (reglas-1) → {Informe.Pct(modelo.P3)}, NDCG@3 {Informe.Num(reglas.Ndcg3, "0.000")} → {Informe.Num(modelo.Ndcg3, "0.000")}";
        var fallos = new List<string>();
        if (modelo.P3 < reglas.P3 + minimaMejoraP3 - EPSILON)
            fallos.Add($"la P@3 no gana a reglas-1 por {Informe.Num(minimaMejoraP3 * 100, "0.#")} puntos (gana por {Informe.Num((modelo.P3 - reglas.P3) * 100, "0.0")})");
        if (modelo.Ndcg3 < reglas.Ndcg3 - EPSILON)
            fallos.Add("la NDCG@3 es peor que la de reglas-1");
        if (versionAnterior != null && !double.IsNaN(versionAnterior.P3))
        {
            comparacion += $", P@3 sin las features nuevas {Informe.Pct(versionAnterior.P3)}";
            if (modelo.P3 < versionAnterior.P3 - maximaPerdidaFrenteAnterior - EPSILON)
                fallos.Add($"la P@3 es peor que la del modelo sin las features nuevas ({Informe.Pct(versionAnterior.P3)}) por más de {Informe.Num(maximaPerdidaFrenteAnterior * 100, "0.0#")} puntos");
        }
        return fallos.Count == 0
            ? new DecisionPuerta(true, comparacion)
            : new DecisionPuerta(false, $"{string.Join(" y ", fallos)}; {comparacion}");
    }
}

public static class Informe
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public static string Pct(double v) => double.IsNaN(v) ? "—" : (v * 100).ToString("0.0", Es) + " %";
    public static string Num(double v, string formato) => double.IsNaN(v) ? "—" : v.ToString(formato, Es);

    public static string Tabla(IEnumerable<ResultadoRanking> filas)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Modelo | P@3 | R@3 | NDCG@3 | Al menos 1 | Sesiones | Cobertura |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (ResultadoRanking r in filas)
            sb.AppendLine($"| {r.Modelo} | {Pct(r.P3)} | {Pct(r.R3)} | {Num(r.Ndcg3, "0.000")} | {Pct(r.AlMenos1)} | {r.Sesiones} | {Pct(r.Cobertura)} |");
        return sb.ToString();
    }

    /// <summary>NestoAPI#683: P@3 (y sesiones) de cada modelo por grupo de estado del cliente.</summary>
    /// <param name="segmentos">Nombre del segmento → resultados de cada modelo en ese segmento (en el orden de las filas).</param>
    public static string TablaEstados(IReadOnlyList<string> modelos, IReadOnlyList<KeyValuePair<string, List<ResultadoRanking>>> segmentos)
    {
        var sb = new StringBuilder();
        sb.Append("| Modelo |");
        foreach (KeyValuePair<string, List<ResultadoRanking>> s in segmentos)
            sb.Append($" {s.Key} ({s.Value.FirstOrDefault()?.Sesiones ?? 0}) |");
        sb.AppendLine();
        sb.Append("|---|");
        foreach (KeyValuePair<string, List<ResultadoRanking>> _ in segmentos) sb.Append("---:|");
        sb.AppendLine();
        for (int i = 0; i < modelos.Count; i++)
        {
            sb.Append($"| {modelos[i]} |");
            foreach (KeyValuePair<string, List<ResultadoRanking>> s in segmentos)
                sb.Append($" {(i < s.Value.Count ? Pct(s.Value[i].P3) : "—")} |");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string TablaSegmentos(IEnumerable<ResultadoRanking> filas)
    {
        var sb = new StringBuilder();
        sb.Append("| Modelo |");
        foreach (string s in Metricas.SEGMENTOS) sb.Append($" {s}: sugeridos | {s}: precisión | {s}: recuperación |");
        sb.AppendLine();
        sb.Append("|---|");
        foreach (string _ in Metricas.SEGMENTOS) sb.Append("---:|---:|---:|");
        sb.AppendLine();
        foreach (ResultadoRanking r in filas)
        {
            sb.Append($"| {r.Modelo} |");
            foreach (string s in Metricas.SEGMENTOS)
            {
                ResultadoSegmento seg = r.PorSegmento[s];
                sb.Append($" {seg.Sugeridos} | {Pct(seg.Precision)} | {Pct(seg.Recuperacion)} |");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
