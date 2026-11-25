namespace NestoAPI.Models.PedidosVenta.ServirJunto
{
    public class ProductoSinStockDTO
    {
        public string ProductoId { get; set; }
        public string ProductoNombre { get; set; }
        public string AlmacenConStock { get; set; }

        /// <summary>
        /// Incidencia 1460 (25/11/26): las unidades disponibles en el almacén del pedido cuando lo deniega
        /// <c>ValidadorMaterialPromocional</c> (para que el guardado de un pedido de tienda deje la muestra en lo que hay). Nunca viaja.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        internal int? Disponibles { get; set; }
    }
}
