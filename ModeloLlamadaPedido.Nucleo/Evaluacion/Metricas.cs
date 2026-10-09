using ModeloLlamadaPedido.Features;

namespace ModeloLlamadaPedido.Evaluacion;

public sealed class ResultadoPrecision
{
    /// <summary>Media de precision@k sobre los grupos (vendedor, mes) con al menos k clientes.</summary>
    public double Media { get; init; }
    /// <summary>Media de la tasa de positivos de esos mismos grupos (lo que daría elegir al azar).</summary>
    public double TasaBase { get; init; }
    public int Grupos { get; init; }
    /// <summary>Media de precision@k de cada vendedor (sobre sus meses).</summary>
    public Dictionary<string, double> PorVendedor { get; init; } = new();
}

public sealed class ResultadoCalibracion
{
    public double ProbabilidadMedia { get; init; }
    public double TasaReal { get; init; }
    /// <summary>Filas con probabilidad ≥ umbral (la «Máxima» del motor usa 0,6).</summary>
    public int PorEncimaUmbral { get; init; }
    public double PrecisionPorEncimaUmbral { get; init; }
}

public static class Metricas
{
    /// <summary>AUC ROC por Mann-Whitney (empates = 0,5). NaN si falta alguna clase.</summary>
    public static double Auc(IReadOnlyList<float> puntuaciones, IReadOnlyList<bool> etiquetas)
    {
        int n = puntuaciones.Count;
        var orden = Enumerable.Range(0, n).OrderBy(i => puntuaciones[i]).ToArray();
        var rangos = new double[n];
        int j = 0;
        while (j < n)
        {
            int k = j;
            while (k + 1 < n && puntuaciones[orden[k + 1]] == puntuaciones[orden[j]]) k++;
            double rango = (j + k) / 2.0 + 1;
            for (int m = j; m <= k; m++) rangos[orden[m]] = rango;
            j = k + 1;
        }
        long positivos = etiquetas.LongCount(e => e);
        long negativos = n - positivos;
        if (positivos == 0 || negativos == 0) return double.NaN;
        double sumaRangos = 0;
        for (int i = 0; i < n; i++) if (etiquetas[i]) sumaRangos += rangos[i];
        return (sumaRangos - positivos * (positivos + 1) / 2.0) / (positivos * (double)negativos);
    }

    /// <summary>
    /// precision@k por vendedor y mes: en cada (vendedor, mes) se queda un contacto por cliente (el primero del mes, como
    /// una lista mensual de clientes), se ordena por la puntuación y se mira cuántos de los k primeros acabaron en pedido.
    /// Los grupos con menos de k clientes no cuentan. Empates: orden estable por cliente.
    /// </summary>
    public static ResultadoPrecision PrecisionEnK(IReadOnlyList<FilaEntrenamiento> filas, IReadOnlyList<float> puntuaciones, int k)
    {
        var grupos = Enumerable.Range(0, filas.Count)
            .GroupBy(i => (filas[i].Vendedor, filas[i].AnnoMes))
            .Select(g => (g.Key.Vendedor, Indices: g
                .GroupBy(i => filas[i].ClienteId)
                .Select(c => c.OrderBy(i => filas[i].Fecha).First())
                .ToList()))
            .Where(g => g.Indices.Count >= k)
            .ToList();

        var porGrupo = grupos.Select(g =>
        {
            var top = g.Indices
                .OrderByDescending(i => puntuaciones[i])
                .ThenBy(i => filas[i].ClienteId, StringComparer.Ordinal)
                .Take(k);
            return (g.Vendedor,
                Precision: top.Count(i => filas[i].Label) / (double)k,
                Base: g.Indices.Count(i => filas[i].Label) / (double)g.Indices.Count);
        }).ToList();

        return new ResultadoPrecision
        {
            Grupos = porGrupo.Count,
            Media = porGrupo.Count == 0 ? double.NaN : porGrupo.Average(p => p.Precision),
            TasaBase = porGrupo.Count == 0 ? double.NaN : porGrupo.Average(p => p.Base),
            PorVendedor = porGrupo.GroupBy(p => p.Vendedor).ToDictionary(g => g.Key, g => g.Average(p => p.Precision))
        };
    }

    public static ResultadoCalibracion Calibracion(IReadOnlyList<float> probabilidades, IReadOnlyList<bool> etiquetas, float umbral = 0.6f)
    {
        var encima = Enumerable.Range(0, probabilidades.Count).Where(i => probabilidades[i] >= umbral).ToList();
        return new ResultadoCalibracion
        {
            ProbabilidadMedia = probabilidades.Count == 0 ? double.NaN : probabilidades.Average(p => (double)p),
            TasaReal = etiquetas.Count == 0 ? double.NaN : etiquetas.Average(e => e ? 1.0 : 0.0),
            PorEncimaUmbral = encima.Count,
            PrecisionPorEncimaUmbral = encima.Count == 0 ? double.NaN : encima.Average(i => etiquetas[i] ? 1.0 : 0.0)
        };
    }
}
