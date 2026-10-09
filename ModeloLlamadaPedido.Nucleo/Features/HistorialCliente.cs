using ModeloLlamadaPedido.Datos;

namespace ModeloLlamadaPedido.Features;

/// <summary>Todo lo que se sabe de un cliente/contacto, ordenado por fecha.</summary>
public sealed class HistorialCliente
{
    public string ClienteId { get; }
    public IReadOnlyList<PedidoDia> Pedidos { get; }
    public IReadOnlyList<RapportCrudo> Rapports { get; }
    public IReadOnlyList<VentaGrupoMes> Ventas { get; }

    public HistorialCliente(string clienteId, IEnumerable<PedidoDia> pedidos, IEnumerable<RapportCrudo> rapports, IEnumerable<VentaGrupoMes> ventas)
    {
        ClienteId = clienteId;
        // Un pedido = un día: se agrupan por si vienen varias filas del mismo día.
        Pedidos = pedidos.GroupBy(p => p.Dia.Date)
            .Select(g => new PedidoDia(clienteId, g.Key, g.Sum(p => p.Importe)))
            .OrderBy(p => p.Dia).ToList();
        Rapports = rapports.OrderBy(r => r.Fecha).ToList();
        Ventas = ventas.ToList();
    }

    private bool[]? _positivos;

    /// <summary>Etiqueta de cada rapport (mismo índice que <see cref="Rapports"/>), calculada una vez.</summary>
    public bool EsPositivo(int indiceRapport)
    {
        _positivos ??= Rapports.Select(r => CalculadoraFeatures.EsPositivo(r, this)).ToArray();
        return _positivos[indiceRapport];
    }

    public static Dictionary<string, HistorialCliente> Agrupar(DatosCrudos datos)
    {
        var pedidos = datos.Pedidos.ToLookup(p => p.ClienteId);
        var rapports = datos.Rapports.ToLookup(r => r.ClienteId);
        var ventas = datos.Ventas.ToLookup(v => v.ClienteId);
        return datos.Rapports.Select(r => r.ClienteId).Distinct()
            .ToDictionary(id => id, id => new HistorialCliente(id, pedidos[id], rapports[id], ventas[id]));
    }
}
