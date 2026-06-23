using NestoAPI.Models.Facturas;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#195 (Fase 3): resultado de procesar la agrupación por PO de una empresa.
    /// Lista las facturas creadas y los grupos que fallaron (cada grupo se procesa de forma
    /// aislada: un fallo en un PO no impide procesar el resto).
    /// </summary>
    public class ResultadoAgrupacionPO
    {
        public IList<CrearFacturaResponseDTO> Facturas { get; } = new List<CrearFacturaResponseDTO>();
        public IList<ErrorAgrupacionPO> Errores { get; } = new List<ErrorAgrupacionPO>();
    }

    public class ErrorAgrupacionPO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string SuPedido { get; set; }
        public string Mensaje { get; set; }
    }
}
