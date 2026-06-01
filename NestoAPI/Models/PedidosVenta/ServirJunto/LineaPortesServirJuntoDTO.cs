namespace NestoAPI.Models.PedidosVenta.ServirJunto
{
    /// <summary>
    /// Datos mínimos de una línea de producto necesarios para calcular, server-side, la base de
    /// portes que quedaría al DESMARCAR "servir junto" (NestoAPI#211 / Nesto#365). Permite avisar al
    /// usuario si al desmarcar aparecen gastos de envío por las líneas sobre pedido.
    /// </summary>
    public class LineaPortesServirJuntoDTO
    {
        public string ProductoId { get; set; }
        public string Almacen { get; set; }
        public short Estado { get; set; }
        public int Cantidad { get; set; }
        public decimal BaseImponible { get; set; }
    }
}
