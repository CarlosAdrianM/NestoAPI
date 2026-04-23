using System.Collections.Generic;

namespace NestoAPI.Models.PedidosVenta.ServirJunto
{
    public class ValidarServirJuntoResponse
    {
        public bool PuedeDesmarcar { get; set; }
        public List<ProductoSinStockDTO> ProductosProblematicos { get; set; }
        public string Mensaje { get; set; }

        // NestoAPI#187: aviso no-bloqueante que el cliente debe mostrar al usuario antes
        // de confirmar el desmarcado (p. ej. comisión contra reembolso por cada envío).
        // Null si no hay nada que avisar.
        public string Aviso { get; set; }
    }
}
