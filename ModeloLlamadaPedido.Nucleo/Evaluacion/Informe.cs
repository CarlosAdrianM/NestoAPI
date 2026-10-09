using System.Globalization;
using System.Text;

namespace ModeloLlamadaPedido.Evaluacion;

public sealed record FilaInforme(string Modelo, double Auc, ResultadoPrecision Precision, ResultadoCalibracion? Calibracion);

public static class Informe
{
    public static readonly string[] VendedoresDestacados = { "MPP", "LHY", "PA", "JGP", "IM", "DLS" };
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public static string Tabla(IReadOnlyList<FilaInforme> filas, int k)
    {
        var vendedores = VendedoresDestacados
            .Where(v => filas.Any(f => f.Precision.PorVendedor.ContainsKey(v)))
            .ToList();
        var sb = new StringBuilder();
        sb.Append($"| Modelo | AUC | P@{k} media | Azar (tasa base) | Grupos vendedor-mes |");
        foreach (string v in vendedores) sb.Append($" P@{k} {v} |");
        sb.AppendLine();
        sb.Append("|---|---:|---:|---:|---:|");
        foreach (string _ in vendedores) sb.Append("---:|");
        sb.AppendLine();
        foreach (FilaInforme f in filas)
        {
            sb.Append($"| {f.Modelo} | {Num(f.Auc, "0.000")} | {Pct(f.Precision.Media)} | {Pct(f.Precision.TasaBase)} | {f.Precision.Grupos} |");
            foreach (string v in vendedores)
                sb.Append($" {(f.Precision.PorVendedor.TryGetValue(v, out double p) ? Pct(p) : "—")} |");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string TablaCalibracion(IReadOnlyList<FilaInforme> filas)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Modelo | Probabilidad media | Tasa real | Filas con p ≥ 0,6 | Acierto con p ≥ 0,6 |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        foreach (FilaInforme f in filas.Where(f => f.Calibracion != null))
        {
            ResultadoCalibracion c = f.Calibracion!;
            sb.AppendLine($"| {f.Modelo} | {Pct(c.ProbabilidadMedia)} | {Pct(c.TasaReal)} | {c.PorEncimaUmbral} | {Pct(c.PrecisionPorEncimaUmbral)} |");
        }
        return sb.ToString();
    }

    public static string Pct(double v) => double.IsNaN(v) ? "—" : (v * 100).ToString("0.0", Es) + " %";
    public static string Num(double v, string formato) => double.IsNaN(v) ? "—" : v.ToString(formato, Es);
}
