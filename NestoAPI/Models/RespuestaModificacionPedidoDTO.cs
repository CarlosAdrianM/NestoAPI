using System.Collections.Generic;

namespace NestoAPI.Models
{
    public class RespuestaModificacionPedidoDTO
    {
        public List<AvisoPedidoDTO> Avisos { get; set; } = new List<AvisoPedidoDTO>();
    }

    public class AvisoPedidoDTO
    {
        public string Tipo { get; set; }
        public string Mensaje { get; set; }
        public object Datos { get; set; }
    }

    public class AvisoEtiquetaPendienteDTO
    {
        public int EtiquetaId { get; set; }
        public DireccionDTO DireccionActual { get; set; }
        public DireccionDTO DireccionNuevoContacto { get; set; }
    }

    public class DireccionDTO
    {
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        public string Telefono { get; set; }
    }
}
