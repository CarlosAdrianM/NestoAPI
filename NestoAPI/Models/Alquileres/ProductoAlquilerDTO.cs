namespace NestoAPI.Models.Alquileres
{
    /// <summary>
    /// Una fila de la lista de productos en alquiler (resultado del SP prdProductosAlquilerLista).
    /// Nombres limpios sin caracteres especiales (Nesto#340, Fase 1C.1).
    /// </summary>
    public class ProductoAlquilerDTO
    {
        public string Empresa { get; set; }
        public string Numero { get; set; }
        public string Nombre { get; set; }
        public int Stock { get; set; }
        public int StockAlquileres { get; set; }
        public int Diferencia { get; set; }
    }
}
