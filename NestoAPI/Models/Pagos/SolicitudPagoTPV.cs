using System.Collections.Generic;

namespace NestoAPI.Models.Pagos
{
    public class SolicitudPagoTPV
    {
        public string Empresa { get; set; } = Constantes.Empresas.EMPRESA_POR_DEFECTO;
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public decimal Importe { get; set; }
        public string Descripcion { get; set; }
        public string Correo { get; set; }
        public string UrlOk { get; set; }
        public string UrlKo { get; set; }
        /// <summary>
        /// NestoAPI#165: selector de método de pago para Redsys.
        /// "C" = solo tarjeta, "z" = solo Bizum, null = todos los métodos habilitados.
        /// </summary>
        public string MetodoPago { get; set; }
        public List<EfectoAPagar> Efectos { get; set; }

        /// <summary>
        /// NestoAPI#436: numero del pedido que se esta cobrando (pedidos de la app). Cuando viene,
        /// el cobro NO se contabiliza como el enlace de pago: al confirmar Redsys entra como
        /// Prepago del pedido y se aplica al facturarlo.
        /// Lo pone el servidor a partir del pedido que acaba de crear, nunca el cliente: si no,
        /// podria pagar 1 EUR por un pedido de 100.
        /// </summary>
        public int? Pedido { get; set; }

        // Campos legacy para compatibilidad con pago individual sin Efectos
        public int? ExtractoClienteId { get; set; }
        public string Documento { get; set; }
        public string Efecto { get; set; }
        public string Vendedor { get; set; }
        public string FormaVenta { get; set; }
        public string Delegacion { get; set; }
        public string TipoApunte { get; set; }
    }
}
