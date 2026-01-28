using System.Collections.Generic;

namespace NestoAPI.Models.Rectificativas
{
    /// <summary>
    /// Respuesta de la operación de copiar factura.
    /// Issue #85
    /// </summary>
    public class CopiarFacturaResponse
    {
        public CopiarFacturaResponse()
        {
            LineasCopiadas = new List<LineaCopiadaDTO>();
        }

        /// <summary>
        /// Número del pedido donde se copiaron las líneas (nuevo o existente)
        /// </summary>
        public int NumeroPedido { get; set; }

        /// <summary>
        /// Número de albarán creado (si se solicitó CrearAlbaranYFactura)
        /// </summary>
        public int? NumeroAlbaran { get; set; }

        /// <summary>
        /// Número de factura creada (si se solicitó CrearAlbaranYFactura)
        /// </summary>
        public string NumeroFactura { get; set; }

        /// <summary>
        /// Detalle de las líneas copiadas con su origen (para trazabilidad)
        /// </summary>
        public List<LineaCopiadaDTO> LineasCopiadas { get; set; }

        /// <summary>
        /// Mensaje descriptivo del resultado
        /// </summary>
        public string Mensaje { get; set; }

        /// <summary>
        /// Indica si la operación fue exitosa
        /// </summary>
        public bool Exitoso { get; set; }
    }

    /// <summary>
    /// Detalle de una línea copiada, incluyendo metadata del origen
    /// para la futura vinculación en LinFacturaVtaRectificacion (Issue #38)
    /// </summary>
    public class LineaCopiadaDTO
    {
        /// <summary>
        /// Número de orden de la línea creada en el pedido destino
        /// </summary>
        public int NumeroLineaNueva { get; set; }

        /// <summary>
        /// Producto copiado
        /// </summary>
        public string Producto { get; set; }

        /// <summary>
        /// Descripción del producto
        /// </summary>
        public string Descripcion { get; set; }

        /// <summary>
        /// Número de factura origen (para vinculación rectificativa)
        /// </summary>
        public string FacturaOrigen { get; set; }

        /// <summary>
        /// Número de línea en la factura origen (para vinculación rectificativa)
        /// </summary>
        public int LineaOrigen { get; set; }

        /// <summary>
        /// Cantidad en la factura original
        /// </summary>
        public decimal CantidadOriginal { get; set; }

        /// <summary>
        /// Cantidad copiada (puede ser negativa si se invirtió)
        /// </summary>
        public decimal CantidadCopiada { get; set; }

        /// <summary>
        /// Precio unitario aplicado
        /// </summary>
        public decimal PrecioUnitario { get; set; }

        /// <summary>
        /// Base imponible de la línea
        /// </summary>
        public decimal BaseImponible { get; set; }
    }
}
