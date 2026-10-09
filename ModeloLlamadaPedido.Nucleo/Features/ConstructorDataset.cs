using ModeloLlamadaPedido.Datos;

namespace ModeloLlamadaPedido.Features;

/// <summary>Una fila por contacto (rapport T/V/W, Estado 0) dentro de la ventana.</summary>
public sealed class Dataset
{
    public List<FilaEntrenamiento> Filas { get; } = new();
}

public static class ConstructorDataset
{
    /// <param name="desde">Primer día de contactos incluidos.</param>
    /// <param name="hasta">Día (excluido) hasta el que se incluyen contactos; debe dejar 7 días cerrados para la etiqueta.</param>
    /// <param name="vendedor">Si se indica, solo filas de ese vendedor (el historial del cliente se usa entero).</param>
    public static Dataset Construir(DatosCrudos datos, DateTime desde, DateTime hasta, string? vendedor)
    {
        Dictionary<string, HistorialCliente> historiales = HistorialCliente.Agrupar(datos);
        var dataset = new Dataset();
        foreach (HistorialCliente h in historiales.Values.OrderBy(x => x.ClienteId, StringComparer.Ordinal))
        {
            foreach (RapportCrudo r in h.Rapports)
            {
                if (r.Fecha < desde || r.Fecha >= hasta) continue;
                if (vendedor != null && !string.Equals(r.Vendedor, vendedor, StringComparison.OrdinalIgnoreCase)) continue;

                FilaEntrenamiento fila = CalculadoraFeatures.Calcular<FilaEntrenamiento>(h, r.Fecha, CalculadoraFeatures.TipoInteraccion(r.Tipo));
                fila.Label = CalculadoraFeatures.EsPositivo(r, h);
                fila.Vendedor = string.IsNullOrEmpty(r.Vendedor) ? "(sin)" : r.Vendedor;
                fila.Fecha = r.Fecha;
                fila.AnnoMes = Claves.AnnoMes(r.Fecha);
                dataset.Filas.Add(fila);
            }
        }
        return dataset;
    }

    /// <summary>
    /// Partición temporal: validación = contactos desde <paramref name="corte"/>; entrenamiento = contactos cuya ventana de
    /// 7 días termina antes del corte (hueco para que ninguna etiqueta de entrenamiento mire dentro de la validación).
    /// </summary>
    public static (List<int> entrenamiento, List<int> validacion) Particion(IReadOnlyList<FilaEntrenamiento> filas, DateTime corte)
    {
        var entrenamiento = new List<int>();
        var validacion = new List<int>();
        for (int i = 0; i < filas.Count; i++)
        {
            DateTime dia = filas[i].Fecha.Date;
            if (dia >= corte) validacion.Add(i);
            else if (dia.AddDays(CalculadoraFeatures.VENTANA_POSITIVO_DIAS) < corte) entrenamiento.Add(i);
        }
        return (entrenamiento, validacion);
    }
}
