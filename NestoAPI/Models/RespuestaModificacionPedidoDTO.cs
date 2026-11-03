using System.Collections.Generic;

namespace NestoAPI.Models
{
    public class RespuestaModificacionPedidoDTO
    {
        public List<AvisoPedidoDTO> Avisos { get; set; } = new List<AvisoPedidoDTO>();
        /// <summary>Nesto#588: «fechas futuras» del pedido tal como ha quedado (la regla de la lista); null = no se ha podido calcular.</summary>
        public bool? tieneFechasFuturas { get; set; }
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
