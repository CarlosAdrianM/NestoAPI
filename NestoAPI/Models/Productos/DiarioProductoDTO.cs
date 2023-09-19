using System.Collections.Generic;

namespace NestoAPI.Models.Productos
{
    public class DiarioProductoDTO
    {
        public string Id { get;set; }
        public string Descripcion { get; set; }
        public bool EstaVacio { get; set; }
        public List<string> Almacenes { get; set; }
    }
}