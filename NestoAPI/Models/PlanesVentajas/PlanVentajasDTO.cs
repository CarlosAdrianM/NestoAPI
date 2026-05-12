using System;
using System.Collections.Generic;

namespace NestoAPI.Models.PlanesVentajas
{
    public class PlanVentajasDTO
    {
        public int Numero { get; set; }
        public string Empresa { get; set; }
        public string EmpresaNombre { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public decimal Importe { get; set; }
        public string Familia { get; set; }
        public int Estado { get; set; }
        public string EstadoDescripcion { get; set; }
        public string Comentarios { get; set; }
        public List<string> Clientes { get; set; } = new List<string>();
    }
}
