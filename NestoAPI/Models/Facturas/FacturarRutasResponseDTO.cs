using System;
using System.Collections.Generic;
using System.Linq;

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
            Albaranes = new List<AlbaranCreadoDTO>();
            Facturas = new List<FacturaCreadaDTO>();
            NotasEntrega = new List<NotaEntregaCreadaDTO>();
        }

        /// <summary>
        /// Número de pedidos procesados (con éxito o con error)
        /// </summary>
        public int PedidosProcesados { get; set; }

        /// <summary>
        /// Lista de albaranes creados.
        /// Algunos pueden tener DatosImpresion rellenos (con bytes del PDF) si deben imprimirse.
        /// </summary>
        public List<AlbaranCreadoDTO> Albaranes { get; set; }

        /// <summary>
        /// Lista de facturas creadas.
        /// Algunas pueden tener DatosImpresion rellenos (con bytes del PDF) si deben imprimirse.
        /// </summary>
        public List<FacturaCreadaDTO> Facturas { get; set; }

        /// <summary>
        /// Lista de notas de entrega creadas.
        /// Las notas de entrega documentan entregas de productos ya facturados o pendientes de facturación.
        /// </summary>
        public List<NotaEntregaCreadaDTO> NotasEntrega { get; set; }

        /// <summary>
        /// Lista de pedidos que tuvieron errores durante el proceso
        /// </summary>
        public List<PedidoConErrorDTO> PedidosConErrores { get; set; }

        /// <summary>
        /// Tiempo total que tomó el proceso
        /// </summary>
        public TimeSpan TiempoTotal { get; set; }

        // Propiedades de conveniencia para contadores (compatibilidad con código existente)

        /// <summary>
        /// Número de albaranes creados (calculado)
        /// </summary>
        public int AlbaranesCreados => Albaranes?.Count ?? 0;

        /// <summary>
        /// Número de facturas creadas (calculado)
        /// </summary>
        public int FacturasCreadas => Facturas?.Count ?? 0;

        /// <summary>
        /// Número de albaranes con datos de impresión (calculado)
        /// </summary>
        public int AlbaranesParaImprimir => Albaranes?.Count(a => a.DatosImpresion != null) ?? 0;

        /// <summary>
        /// Número de facturas con datos de impresión (calculado)
        /// </summary>
        public int FacturasParaImprimir => Facturas?.Count(f => f.DatosImpresion != null) ?? 0;

        /// <summary>
        /// Número de notas de entrega creadas (calculado)
        /// </summary>
        public int NotasEntregaCreadas => NotasEntrega?.Count ?? 0;

        /// <summary>
        /// Número de notas de entrega con datos de impresión (calculado)
        /// </summary>
        public int NotasEntregaParaImprimir => NotasEntrega?.Count(n => n.DatosImpresion != null) ?? 0;
    }
}
