using NestoAPI.Models.Facturas;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// DTO que contiene los documentos listos para imprimir de un pedido.
    /// Incluye facturas, albaranes y notas de entrega según corresponda.
    /// </summary>
    public class DocumentosImpresionPedidoDTO
    {
        public DocumentosImpresionPedidoDTO()
        {
            Facturas = new List<FacturaCreadaDTO>();
            Albaranes = new List<AlbaranCreadoDTO>();
            NotasEntrega = new List<NotaEntregaCreadaDTO>();
        }

        /// <summary>
        /// Facturas generadas para el pedido (si se facturó y no es FIN_DE_MES)
        /// </summary>
        public List<FacturaCreadaDTO> Facturas { get; set; }

        /// <summary>
        /// Albaranes generados para el pedido
        /// </summary>
        public List<AlbaranCreadoDTO> Albaranes { get; set; }

        /// <summary>
        /// Notas de entrega generadas para el pedido
        /// </summary>
        public List<NotaEntregaCreadaDTO> NotasEntrega { get; set; }

        /// <summary>
        /// Indica si hay algún documento para imprimir
        /// </summary>
        public bool HayDocumentosParaImprimir =>
            (Facturas?.Any(f => f.DatosImpresion != null) ?? false) ||
            (Albaranes?.Any(a => a.DatosImpresion != null) ?? false) ||
            (NotasEntrega?.Any(n => n.DatosImpresion != null) ?? false);

        /// <summary>
        /// Total de documentos que se imprimirán (considerando copias)
        /// </summary>
        public int TotalDocumentosParaImprimir
        {
            get
            {
                int total = 0;
                total += Facturas?.Where(f => f.DatosImpresion != null).Sum(f => f.DatosImpresion.NumeroCopias) ?? 0;
                total += Albaranes?.Where(a => a.DatosImpresion != null).Sum(a => a.DatosImpresion.NumeroCopias) ?? 0;
                total += NotasEntrega?.Where(n => n.DatosImpresion != null).Sum(n => n.DatosImpresion.NumeroCopias) ?? 0;
                return total;
            }
        }

        /// <summary>
        /// Tipo de documento principal que se generó (para información al usuario)
        /// </summary>
        public string TipoDocumentoPrincipal { get; set; }

        /// <summary>
        /// Mensaje descriptivo de lo que se generó
        /// </summary>
        public string Mensaje { get; set; }
    }
}
