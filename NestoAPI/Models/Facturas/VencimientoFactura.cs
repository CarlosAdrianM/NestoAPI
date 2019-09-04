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
        public string TextoPagado
        {
            get
            {
                return ImportePendiente == 0 ? "Pagado" :
                    ImportePendiente == Importe ? "Pendiente de pago" :
                    String.Format("Pendientes {0:C2}", ImportePendiente);
            }
        }
        public string Iban { get; set; }
    }
}