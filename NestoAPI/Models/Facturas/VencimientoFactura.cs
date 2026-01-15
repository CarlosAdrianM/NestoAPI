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

        public string TextoPagado
        {
            get
            {
                // Issue #66: No mostrar estado de pago para documentos que no son facturas
                if (OcultarEstadoPago)
                {
                    return string.Empty;
                }

                return ImportePendiente == 0 ? "Pagado" :
                    ImportePendiente == Importe ? "Pendiente de pago" :
                    String.Format("Pendientes {0:C2}", ImportePendiente);
            }
        }
        public string Iban { get; set; }
    }
}