namespace NestoAPI.Models.Productos
{
    public class ControlStockAlmacenModel : ControlStockBase
    {
        public string Almacen { get; set; }        
        public string Estacionalidad { get; set; }
        public string Categoria { get; set; }
        public int Multiplos { get; set; }
        public int StockMaximoActual { get; set; }
        public bool YaExiste { get; internal set; }
    }
}