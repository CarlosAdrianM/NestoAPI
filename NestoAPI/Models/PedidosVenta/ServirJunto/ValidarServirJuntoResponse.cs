using System.Collections.Generic;

namespace NestoAPI.Models.PedidosVenta.ServirJunto
{
    public class ValidarServirJuntoResponse
    {
        public bool PuedeDesmarcar { get; set; }
        public List<ProductoSinStockDTO> ProductosProblematicos { get; set; }
        public string Mensaje { get; set; }
    }
}
