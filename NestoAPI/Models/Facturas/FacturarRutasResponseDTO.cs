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
            FacturasPorPO = new List<string>();
            ErroresPorPO = new List<string>();
            ReembolsosDesfasados = new List<ReembolsoDesfasadoDTO>();
            CartasChequeRegalo = new List<CartaChequeRegaloDTO>();
            ImpresosEnPacking = new List<DocumentoEnPapelDTO>();
            PendientesEnPacking = new List<DocumentoEnPapelDTO>();
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

        /// <summary>
        /// NestoAPI#195 (Fase 3): números de factura creadas por agrupación de PO
        /// (pedidos con MantenerJunto + mismo P.O. agrupados en una sola factura), que se
        /// procesan ANTES de la facturación normal de la ruta.
        /// </summary>
        public List<string> FacturasPorPO { get; set; }

        /// <summary>
        /// NestoAPI#195 (Fase 3): errores al agrupar/facturar por P.O. (uno por grupo que falló;
        /// no bloquean ni la facturación de otros PO ni la facturación normal de la ruta).
        /// </summary>
        public List<string> ErroresPorPO { get; set; }

        /// <summary>
        /// NestoAPI#810: envíos del reparto propio sin entregar de los pedidos facturados cuyo reembolso no es lo que queda por
        /// cobrar de sus facturas (p. ej. tras una ampliación). No se cambian solos: el cliente pregunta y llama a
        /// PUT api/EnviosAgencias/{Envio}/Reembolso con ReembolsoFacturado. Vacía si no hay ninguno.
        /// </summary>
        public List<ReembolsoDesfasadoDTO> ReembolsosDesfasados { get; set; }

        /// <summary>
        /// NestoAPI#838: las cartas en papel de los cheques regalo que han generado las facturas (una por cheque). Con el
        /// interruptor de impresión en servidor salen detrás de su factura.
        /// </summary>
        public List<CartaChequeRegaloDTO> CartasChequeRegalo { get; set; }

        /// <summary>
        /// NestoAPI#828 (S4): en Facturar rutas de agencias, los documentos en papel que ya salieron hoy en el packing (al hacer la
        /// etiqueta): no entran en esta tanda. Vacía en la ruta propia o sin la tabla dbo.DocumentosPapelPacking.
        /// </summary>
        public List<DocumentoEnPapelDTO> ImpresosEnPacking { get; set; }

        /// <summary>
        /// NestoAPI#828 (S4): los documentos en papel de hoy que NO salieron en el packing (la impresora falló): con su pedido y su
        /// envío, para reimprimirlos (repetir la etiqueta, «Reintentar impresión» en Ariadna) o meterlos en la caja.
        /// </summary>
        public List<DocumentoEnPapelDTO> PendientesEnPacking { get; set; }

        /// <summary>
        /// NestoAPI#828 (S4): el resumen para el usuario: «3 facturas en papel ya impresas en el packing.», lo pendiente del packing
        /// y lo que sale en esta tanda con su envío para localizar la caja. Null si no hay nada que decir.
        /// </summary>
        public string ResumenPapelPacking { get; set; }

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
