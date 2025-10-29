using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Response con el resultado de facturar rutas
    /// </summary>
    public class FacturarRutasResponseDTO
    {
        public FacturarRutasResponseDTO()
        {
            PedidosConErrores = new List<PedidoConErrorDTO>();
        }

        /// <summary>
        /// Número de pedidos procesados (con éxito o con error)
        /// </summary>
        public int PedidosProcesados { get; set; }

        /// <summary>
        /// Número de albaranes creados
        /// </summary>
        public int AlbaranesCreados { get; set; }

        /// <summary>
        /// Número de facturas creadas
        /// </summary>
        public int FacturasCreadas { get; set; }

        /// <summary>
        /// Número de facturas impresas
        /// </summary>
        public int FacturasImpresas { get; set; }

        /// <summary>
        /// Número de albaranes impresos
        /// </summary>
        public int AlbaranesImpresos { get; set; }

        /// <summary>
        /// Lista de pedidos que tuvieron errores durante el proceso
        /// </summary>
        public List<PedidoConErrorDTO> PedidosConErrores { get; set; }

        /// <summary>
        /// Tiempo total que tomó el proceso
        /// </summary>
        public TimeSpan TiempoTotal { get; set; }
    }
}
