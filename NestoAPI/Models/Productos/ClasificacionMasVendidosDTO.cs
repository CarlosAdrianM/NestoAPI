namespace NestoAPI.Models.Productos
{
    /// <summary>
    /// NestoAPI#644: una fila de la clasificación de más vendidos (tabla ClasificacionMasVendidos),
    /// tal como la descarga la tienda cada noche para ordenar por «Relevancia».
    /// </summary>
    public class ClasificacionMasVendidosDTO
    {
        /// <summary>Número de producto, recortado (Trim), igual que en Nesto_sync.</summary>
        public string Producto { get; set; }

        /// <summary>1 = el que más se vende.</summary>
        public int Posicion { get; set; }
    }
}
