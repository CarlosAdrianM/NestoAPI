using NestoAPI.Models.Agencias;
using System;

namespace NestoAPI.Models
{
    /// <summary>
    /// DTO para devolver el último envío de un cliente con información de seguimiento.
    /// Diseñado para TiendasNuevaVision - Issue #70
    /// </summary>
    public class UltimoEnvioClienteDTO
    {
        public int Pedido { get; set; }
        public DateTime Fecha { get; set; }
        public DateTime? FechaEntrega { get; set; }
        public int AgenciaId { get; set; }
        public string AgenciaNombre { get; set; }
        public string AgenciaIdentificador { get; set; }
        public string NumeroSeguimiento { get; set; }
        public string CodigoPostal { get; set; }
        public string Cliente { get; set; }
        public short Estado { get; set; }
        public short Bultos { get; set; }
        public string Observaciones { get; set; }

        /// <summary>
        /// URL completa de seguimiento de la agencia de transporte.
        /// Se calcula dinámicamente según la agencia.
        /// </summary>
        public string UrlSeguimiento
        {
            get
            {
                // NestoAPI#240: delega en RegistroSeguimientoAgencias (un único sitio por agencia).
                // Este DTO conserva su contrato: null si no hay número de seguimiento, si la agencia
                // no se conoce, o si faltan datos para construir la URL.
                if (string.IsNullOrEmpty(NumeroSeguimiento))
                {
                    return null;
                }
                var datos = new DatosSeguimientoEnvio
                {
                    AgenciaNombre = AgenciaNombre,
                    Identificador = AgenciaIdentificador,
                    CodigoSeguimiento = NumeroSeguimiento,
                    CodigoPostal = CodigoPostal,
                    Cliente = Cliente,
                    Pedido = Pedido
                };
                return RegistroSeguimientoAgencias.ConstruirUrl(datos);
            }
        }

        /// <summary>
        /// Descripción legible del estado del envío.
        /// </summary>
        public string EstadoDescripcion
        {
            get
            {
                switch (Estado)
                {
                    case 0:
                        return "Pendiente";
                    case 1:
                        return "Tramitado";
                    case 2:
                        return "En tránsito";
                    case 3:
                        return "Entregado";
                    case 4:
                        return "Incidencia";
                    default:
                        return "Desconocido";
                }
            }
        }
    }
}
