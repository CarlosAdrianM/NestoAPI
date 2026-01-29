using System.Collections.Generic;

namespace NestoAPI.Models.Rectificativas
{
    /// <summary>
    /// Request para copiar líneas de una factura a un pedido nuevo o existente.
    /// Útil para crear rectificativas rápidas o traspasar facturas entre clientes.
    /// Issue #85, #279
    /// </summary>
    public class CopiarFacturaRequest
    {
        /// <summary>
        /// Empresa de la factura origen
        /// </summary>
        public string Empresa { get; set; }

        /// <summary>
        /// Cliente de la factura origen
        /// </summary>
        public string Cliente { get; set; }

        /// <summary>
        /// Número de factura a copiar (ej: "NV26/001234").
        /// Para una sola factura. Se ignora si NumerosFactura tiene elementos.
        /// </summary>
        public string NumeroFactura { get; set; }

        /// <summary>
        /// Lista de números de factura a copiar (para selección múltiple).
        /// Si tiene elementos, se ignora NumeroFactura.
        /// Issue #279 - SelectorFacturas
        /// </summary>
        public List<string> NumerosFactura { get; set; }

        /// <summary>
        /// Si true, agrupa todas las facturas en una sola rectificativa.
        /// Si false, crea una rectificativa por cada factura.
        /// Solo aplica cuando NumerosFactura tiene más de un elemento.
        /// Issue #279
        /// </summary>
        public bool AgruparEnUnaRectificativa { get; set; } = true;

        /// <summary>
        /// Si true, invierte el signo de las cantidades (para crear rectificativa/abono)
        /// </summary>
        public bool InvertirCantidades { get; set; }

        /// <summary>
        /// Si true, anade las lineas al pedido original en lugar de crear uno nuevo
        /// </summary>
        public bool AnadirAPedidoOriginal { get; set; }

        /// <summary>
        /// Si true, mantiene precios/descuentos/IVA originales.
        /// Si false, recalcula según condiciones del cliente destino.
        /// Solo aplica si ClienteDestino es diferente al Cliente origen.
        /// </summary>
        public bool MantenerCondicionesOriginales { get; set; } = true;

        /// <summary>
        /// Si true, después de copiar crea albarán y factura automáticamente.
        /// Para mismo cliente con InvertirCantidades=true: solo factura las negativas.
        /// </summary>
        public bool CrearAlbaranYFactura { get; set; }

        /// <summary>
        /// Cliente destino. Si es null o vacío, se usa el mismo cliente origen.
        /// </summary>
        public string ClienteDestino { get; set; }

        /// <summary>
        /// Contacto del cliente destino. Requerido si ClienteDestino es diferente.
        /// </summary>
        public string ContactoDestino { get; set; }

        /// <summary>
        /// Indica si el cliente destino es diferente al origen
        /// </summary>
        public bool EsCambioCliente => !string.IsNullOrWhiteSpace(ClienteDestino) && ClienteDestino.Trim() != Cliente?.Trim();

        /// <summary>
        /// Si true, crea DOS operaciones en un solo clic:
        /// 1. Rectificativa (abono) al cliente ORIGEN de la factura
        /// 2. Factura nueva (cargo) al cliente DESTINO seleccionado
        ///
        /// Requiere que ClienteDestino esté informado.
        /// Ignora InvertirCantidades (siempre hace abono y cargo).
        /// </summary>
        public bool CrearAbonoYCargo { get; set; }

        /// <summary>
        /// Comentarios a añadir en el pedido creado.
        /// Se concatenan con el comentario automático de trazabilidad.
        /// </summary>
        public string Comentarios { get; set; }
    }
}
