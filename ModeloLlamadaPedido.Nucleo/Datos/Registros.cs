namespace ModeloLlamadaPedido.Datos;

/// <summary>Un rapport de SeguimientoCliente (Estado 0 = se habló; tipo T/V/W).</summary>
public sealed record RapportCrudo(string ClienteId, DateTime Fecha, string Tipo, bool Pedido, string Vendedor);

/// <summary>Un pedido = un día distinto (CabPedidoVta.Fecha) con líneas TipoLinea 1, base &gt; 0, NotaEntrega 0, Estado ≥ -1.</summary>
public sealed record PedidoDia(string ClienteId, DateTime Dia, decimal Importe);

/// <summary>Cantidad vendida por cliente, mes (AAAAMM) y grupo+subgrupo (sin MMP).</summary>
public sealed record VentaGrupoMes(string ClienteId, int AnnoMes, string GrupoSubgrupo, decimal Cantidad);

/// <summary>
/// NestoAPI#618: lo comprado de un grupo+subgrupo (sin MMP) en un día (pedido = día, como <see cref="PedidoDia"/>), con su base
/// imponible. Sirve para las features de relación cliente↔subgrupo y para la etiqueta «pedido en 7 días que incluya el subgrupo».
/// </summary>
public sealed record PedidoSubgrupoDia(string ClienteId, DateTime Dia, string GrupoSubgrupo, decimal Importe);

public static class Claves
{
    public static string ClienteId(string cliente, string contacto) => $"{cliente.Trim()}/{contacto.Trim()}";
    public static int AnnoMes(DateTime fecha) => fecha.Year * 100 + fecha.Month;
}
