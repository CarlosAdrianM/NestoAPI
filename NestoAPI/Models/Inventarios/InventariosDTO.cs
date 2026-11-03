using NestoAPI.Infraestructure.Productos;
using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Inventarios
{
    /// <summary>NestoAPI#788: el cuerpo del 404 (sin productos) y del 409 (los productos que comparten el código).</summary>
    public class ProductoLeidoInventarioDTO
    {
        public string Message { get; set; }
        public List<ProductoPorCodigoBarrasDTO> Productos { get; set; } = new List<ProductoPorCodigoBarrasDTO>();
    }

    /// <summary>NestoAPI#788: lo pendiente de contabilizar (Estado 1, sin traspaso) de un usuario en un día.</summary>
    public class InventarioPendienteDTO
    {
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
        public int Filas { get; set; }
        public int StockReal { get; set; }
        public int StockCalculado { get; set; }
        public int FilasPorCodigoBarras { get; set; }
        public int FilasTecleadas { get; set; }
        /// <summary>Las que no dicen cómo se leyeron (las de antes de #788, Ariadna vieja, scripts).</summary>
        public int FilasSinMarca { get; set; }
    }
}
