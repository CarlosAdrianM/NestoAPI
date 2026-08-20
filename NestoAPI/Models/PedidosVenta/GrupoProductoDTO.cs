namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// NestoAPI#352: grupo de producto para la lista de selección (el grupo por el que
    /// comisiona una línea de inmovilizado lo elige quien mete el pedido).
    /// </summary>
    public class GrupoProductoDTO
    {
        public string Codigo { get; set; }
        public string Nombre { get; set; }
    }
}
