using System.Collections.Generic;

namespace NestoAPI.Models.Productos
{
    /// <summary>
    /// NestoAPI#249: conjunto de grupos alternativos por los que puede comisionar un producto marcado
    /// (además del de su ficha). Lo mantiene Nesto desde la ficha del producto; lista vacía = desmarcar.
    /// </summary>
    public class ProductoGruposComisionablesDTO
    {
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public List<string> Grupos { get; set; }
    }
}
