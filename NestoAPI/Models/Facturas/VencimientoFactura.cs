using System;

namespace NestoAPI.Models.Facturas
{
    public class VencimientoFactura
    {
        public string CCC { get; set; }
        public string FormaPago { get; set; }
        public DateTime Vencimiento { get; set; }
        public decimal Importe { get; set; }
        public decimal ImportePendiente { get; set; }

        /// <summary>
        /// Issue #66: Para pedidos/proformas/notas de entrega, ocultar el estado de pago
        /// porque no tiene sentido (no hay pagos registrados contra un pedido, solo contra facturas).
        /// </summary>
        public bool OcultarEstadoPago { get; set; }

        /// <summary>
        /// Indica si este vencimiento tiene un impagado pendiente (TipoApunte=4 con ImportePdte!=0).
        /// </summary>
        public bool EsImpagado { get; set; }

        /// <summary>
        /// Importe de gastos bancarios asociados al impagado.
        /// </summary>
        public decimal GastosImpagado { get; set; }

        public string TextoPagado
        {
            get
            {
                // Issue #66: No mostrar estado de pago para documentos que no son facturas
                if (OcultarEstadoPago)
                {
                    return string.Empty;
                }

                if (EsImpagado)
                {
                    return GastosImpagado > 0
                        ? string.Format("Impagado ({0:C2} + {1:C2} gastos)", Importe - GastosImpagado, GastosImpagado)
                        : "Impagado";
                }

                return ImportePendiente == 0 ? "Pagado" :
                    ImportePendiente == Importe ? "Pendiente de pago" :
                    String.Format("Pendientes {0:C2}", ImportePendiente);
            }
        }
        public string Iban { get; set; }
    }
}
