namespace NestoAPI.Models.Productos
{
    /// <summary>
    /// Candidato devuelto cuando un código de barras corresponde a varios productos.
    /// El cliente muestra un selector y reintenta la consulta con el número elegido.
    /// </summary>
    public class ProductoCodigoBarrasDuplicadoDTO
    {
        public string producto { get; set; }
        public string nombre { get; set; }
    }
}
