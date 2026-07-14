using System;

namespace NestoAPI.Models.Ventas
{
    /// <summary>
    /// Ventas de un cliente agrupadas por producto (grid de ventas de la ficha de cliente de Nesto).
    /// Los nombres coinciden con lineaVentaAgrupada del cliente para deserializar sin mapeos (Nesto#340, 1C.8).
    /// </summary>
    public class VentaProductoClienteDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public int Cantidad { get; set; }
        public DateTime FechaUltVenta { get; set; }
        public string SubGrupo { get; set; }
        public string Familia { get; set; }
    }
}
