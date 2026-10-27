using System.Globalization;
using System.Reflection;
using System.Text;
using ModeloSugerenciasProductos.Features;

namespace ModeloSugerenciasProductos.Aceptacion;

/// <summary>
/// NestoAPI#671 (corte 2e): las features del modelo 1 de un candidato en el momento en que se enseñó, en texto
/// («Nombre=valor|Nombre=valor», cultura invariante), para SugerenciasProductosSombra.Features. Así el modelo 2 se entrena con
/// lo que de verdad había (lift del par, días frente a la mediana, frecuencia del cliente, popularidad, estado…) sin
/// recalcularlo después (con fuga). Solo las propiedades float y string de <see cref="ModeloSugerenciaEntrada"/> (sin Cliente
/// ni Producto, que ya están en la fila): al leer, lo que no se conoce se ignora y lo que falta queda con su valor por defecto.
/// </summary>
public static class InstantaneaFeatures
{
    private static readonly PropertyInfo[] Propiedades = typeof(ModeloSugerenciaEntrada)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && (p.PropertyType == typeof(float) || p.PropertyType == typeof(string)))
        .Where(p => p.Name != nameof(ModeloSugerenciaEntrada.Cliente) && p.Name != nameof(ModeloSugerenciaEntrada.Producto))
        .OrderBy(p => p.Name, StringComparer.Ordinal)
        .ToArray();

    private static readonly Dictionary<string, PropertyInfo> PorNombre = Propiedades.ToDictionary(p => p.Name, StringComparer.Ordinal);

    public static string Serializar(ModeloSugerenciaEntrada entrada)
    {
        if (entrada == null) throw new ArgumentNullException(nameof(entrada));
        var sb = new StringBuilder();
        foreach (PropertyInfo p in Propiedades)
        {
            object? valor = p.GetValue(entrada);
            string texto = valor is float f
                ? f.ToString("R", CultureInfo.InvariantCulture)
                : Limpio(valor as string);
            if (p.PropertyType == typeof(float) && texto == "0") continue; // los ceros sobran (valor por defecto al leer)
            if (sb.Length > 0) sb.Append('|');
            sb.Append(p.Name).Append('=').Append(texto);
        }
        return sb.ToString();
    }

    /// <summary>Null si no hay texto. Nunca lanza: lo que no se entiende se ignora.</summary>
    public static ModeloSugerenciaEntrada? Leer(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var entrada = new ModeloSugerenciaEntrada();
        foreach (string par in texto!.Split('|'))
        {
            int igual = par.IndexOf('=');
            if (igual <= 0 || !PorNombre.TryGetValue(par.Substring(0, igual), out PropertyInfo? p)) continue;
            string valor = par.Substring(igual + 1);
            if (p.PropertyType == typeof(string))
            {
                p.SetValue(entrada, valor);
            }
            else if (float.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            {
                p.SetValue(entrada, f);
            }
        }
        return entrada;
    }

    private static string Limpio(string? texto) => (texto ?? string.Empty).Replace("|", " ").Replace("=", " ").Trim();
}
