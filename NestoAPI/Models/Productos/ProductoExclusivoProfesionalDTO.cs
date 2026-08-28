namespace NestoAPI.Models.Productos
{
    /// <summary>
    /// NestoAPI#421: marca de "exclusivo profesional" de un producto. El producto se ve en la
    /// tienda online, pero sin precio ni botón de compra para quien no sea del grupo profesional.
    ///
    /// Es un dato de la ficha y se mantiene desde la pantalla de producto de Nesto. NO se deduce
    /// de las categorías: los subgrupos EP* (COS/EPC, APA/EXP, PEL/EXP...) son categorías
    /// navegables normales y sus productos sí se venden al público.
    /// </summary>
    public class ProductoExclusivoProfesionalDTO
    {
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public bool ExclusivoProfesional { get; set; }
    }
}
