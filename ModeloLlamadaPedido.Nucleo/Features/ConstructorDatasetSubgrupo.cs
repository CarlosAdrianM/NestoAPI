using ModeloLlamadaPedido.Datos;

namespace ModeloLlamadaPedido.Features;

/// <summary>
/// NestoAPI#618: dataset del modelo por subgrupo. Por cada contacto (rapport T/V/W, Estado 0) de la ventana, una fila por
/// subgrupo comprado en los 24 meses anteriores (como mucho <see cref="CalculadoraFeaturesSubgrupo.MAX_COMPRADOS_POR_CONTACTO"/>)
/// más una muestra de subgrupos no comprados del catálogo; positiva si en los 7 días siguientes hay un pedido que lleva ese
/// subgrupo. No hace falta saber de qué se habló.
/// </summary>
public static class ConstructorDatasetSubgrupo
{
    public static List<FilaEntrenamientoSubgrupo> Construir(DatosCrudos datos, DateTime desde, DateTime hasta, string? vendedor,
        int maxComprados = CalculadoraFeaturesSubgrupo.MAX_COMPRADOS_POR_CONTACTO, int noComprados = CalculadoraFeaturesSubgrupo.NO_COMPRADOS_POR_CONTACTO)
    {
        Dictionary<string, HistorialCliente> historiales = HistorialCliente.Agrupar(datos);
        List<string> catalogo = CalculadoraFeaturesSubgrupo.Catalogo(datos.PedidosSubgrupo);
        var filas = new List<FilaEntrenamientoSubgrupo>();
        foreach (HistorialCliente h in historiales.Values.OrderBy(x => x.ClienteId, StringComparer.Ordinal))
        {
            foreach (RapportCrudo r in h.Rapports)
            {
                if (r.Fecha < desde || r.Fecha >= hasta) continue;
                if (vendedor != null && !string.Equals(r.Vendedor, vendedor, StringComparison.OrdinalIgnoreCase)) continue;

                ModeloContactoEntrada general = CalculadoraFeatures.Calcular<ModeloContactoEntrada>(h, r.Fecha, CalculadoraFeatures.TipoInteraccion(r.Tipo));
                ResumenSubgrupos resumen = CalculadoraFeaturesSubgrupo.Resumir(h.PedidosSubgrupo, r.Fecha);
                HashSet<string> enVentana = CalculadoraFeaturesSubgrupo.SubgruposEnVentana(h.PedidosSubgrupo, r.Fecha);
                foreach (string gs in CalculadoraFeaturesSubgrupo.Candidatos(resumen, catalogo, h.ClienteId, r.Fecha, maxComprados, noComprados))
                {
                    FilaEntrenamientoSubgrupo fila = CalculadoraFeaturesSubgrupo.Calcular<FilaEntrenamientoSubgrupo>(general, resumen, gs);
                    fila.Label = enVentana.Contains(gs);
                    fila.Vendedor = string.IsNullOrEmpty(r.Vendedor) ? "(sin)" : r.Vendedor;
                    fila.Fecha = r.Fecha;
                    fila.AnnoMes = Claves.AnnoMes(r.Fecha);
                    filas.Add(fila);
                }
            }
        }
        return filas;
    }
}
