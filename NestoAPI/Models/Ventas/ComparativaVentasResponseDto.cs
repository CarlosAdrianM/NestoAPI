using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Ventas
{
    public class ComparativaVentasResponseDto
    {
        public DateTime FechaDesdeActual { get; set; }
        public DateTime FechaHastaActual { get; set; }
        public DateTime FechaDesdeAnterior { get; set; }
        public DateTime FechaHastaAnterior { get; set; }

        public List<ComparativaVentaDto> Datos { get; set; }
    }
}
