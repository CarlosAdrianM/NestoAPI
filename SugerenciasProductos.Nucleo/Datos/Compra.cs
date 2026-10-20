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
}
