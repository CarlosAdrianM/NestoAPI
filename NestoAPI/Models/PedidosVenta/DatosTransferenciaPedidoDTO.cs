namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// Sugerencia 396 de Novedades: lo que necesita el cliente para pagar por transferencia un pedido
    /// prepago. GET api/PedidosVenta/{empresa}/{numero}/DatosTransferencia.
    /// </summary>
    public class DatosTransferenciaPedidoDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>Nº de cliente sin espacios.</summary>
        public string Cliente { get; set; }
        /// <summary>IBAN formateado de la empresa (si hay varias cuentas, «ES.. o ES..»). Null si no hay.</summary>
        public string Iban { get; set; }
        /// <summary>Beneficiario de la transferencia (Empresas.Nombre).</summary>
        public string Titular { get; set; }
        /// <summary>«Cliente 29606 - Pedido 927160».</summary>
        public string Concepto { get; set; }
        /// <summary>Total del pedido con IVA (el mismo que enseña Nesto).</summary>
        public decimal Importe { get; set; }
        /// <summary>Los datos listos para pegar en un correo o un WhatsApp, una línea por dato.</summary>
        public string Texto { get; set; }
    }
}
