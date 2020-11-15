namespace NestoAPI.Models.Picking
{
    public class ModulosPicking
    {
        public IRellenadorStocksService rellenadorStocks { get; set; }
        public IRellenadorPickingService rellenadorPicking { get; set; }
        public IRellenadorUbicacionesService rellenadorUbicaciones { get; set; }
        public IFinalizadorPicking finalizador { get; set; }
    }
}