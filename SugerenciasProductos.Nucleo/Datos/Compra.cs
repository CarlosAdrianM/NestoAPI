namespace ModeloSugerenciasProductos.Datos;

/// <summary>
/// Una compra: lo que se llevó un cliente de un producto en un día (albaranes, LinPedidoVta Estado ≥ 2). Es la misma fila que
/// lee la API para el recordatorio de reposición (#532) y el job de sugerencias (#670): cliente, producto, día, base imponible
/// y unidades.
/// </summary>
public sealed class Compra
{
    public string Cliente { get; set; } = "";
    public string Producto { get; set; } = "";
    public DateTime Dia { get; set; }
    public decimal BaseImponible { get; set; }
    public int Cantidad { get; set; }
}

/// <summary>
/// La ficha de un producto que importa para sugerir. En la API, FichaProductoSugerencia hereda de esta clase (así el núcleo
/// mira las mismas fichas sin copiarlas).
/// </summary>
public class FichaProducto
{
    public string Producto { get; set; } = "";
    public string? Nombre { get; set; }
    public string? Grupo { get; set; }
    public string? SubGrupo { get; set; }
    /// <summary>La marca (Productos.Familia). La diversidad deja como mucho una sugerencia por familia.</summary>
    public string? Familia { get; set; }
    public short Estado { get; set; }
    public bool Ficticio { get; set; }
}

/// <summary>Lo leído de la BD para entrenar: compras de la ventana y fichas de todos los productos.</summary>
public sealed class DatosCrudos
{
    public List<Compra> Compras { get; } = new();
    public Dictionary<string, FichaProducto> Fichas { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>NestoAPI#683: cliente → Clientes.Estado de HOY del contacto principal.</summary>
    public Dictionary<string, short?> EstadosClientes { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>NestoAPI#683: cambios de estado del contacto principal (SeguimientoCambiosEstadoCliente).</summary>
    public List<Features.CambioEstadoCliente> CambiosEstado { get; } = new();
    /// <summary>
    /// NestoAPI#683: si se ha podido leer el historial de cambios de estado. Sin él, el estado de cada instantánea sería el de
    /// hoy (fuga: hay estados que dependen de lo que el cliente compró después, como «9» o «93») y el modelo no pasa la puerta.
    /// </summary>
    public bool HistorialEstados { get; set; }
    /// <summary>NestoAPI#683: por qué no se ha podido leer el historial (null si se ha leído).</summary>
    public string? MotivoSinHistorialEstados { get; set; }

    public Features.HistorialEstadosCliente Estados() => new(EstadosClientes, HistorialEstados ? CambiosEstado : null);
}
