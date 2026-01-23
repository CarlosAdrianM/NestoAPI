using System;
using System.Net;

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
                if (string.IsNullOrEmpty(NumeroSeguimiento))
                {
                    return null;
                }

                switch (AgenciaNombre)
                {
                    case "ASM":
                        return !string.IsNullOrEmpty(CodigoPostal)
                            ? $"https://mygls.gls-spain.es/e/{NumeroSeguimiento}/{CodigoPostal}"
                            : null;

                    case "OnTime":
                        if (!string.IsNullOrEmpty(Cliente) && Pedido > 0)
                        {
                            string referencia = WebUtility.UrlEncode(Cliente.Trim() + "-" + Pedido.ToString());
                            return $"https://ontimegts.alertran.net/gts/pub/clielocserv.seam?cliente=02890107&referencia={referencia}";
                        }
                        return null;

                    case "Correos Express":
                        return $"https://s.correosexpress.com/c?n={NumeroSeguimiento}";

                    case "Sending":
                        return !string.IsNullOrEmpty(AgenciaIdentificador)
                            ? $"https://info.sending.es/fgts/pub/locNumServ.seam?cliente={AgenciaIdentificador}&localizador={NumeroSeguimiento}"
                            : null;

                    default:
                        return null;
                }
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
