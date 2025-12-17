namespace NestoAPI.Models.Productos
{
    public class ProductoControlStockDTO
    {
        public string ProductoId { get; set; }
        public string Nombre { get; set; }
        public int StockMinimoActual { get; set; }
        public int StockMinimoCalculado { get; set; }
        public int StockMaximoActual { get; set; }
        public int StockMaximoCalculado { get; set; }
        public bool YaExiste { get; set; }
        public string Categoria { get; set; }
        public string Estacionalidad { get; set; }
        public int Multiplos { get; set; }
    }
}
