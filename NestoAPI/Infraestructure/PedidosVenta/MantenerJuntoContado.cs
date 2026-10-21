using System;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#686 (pedido 928220): un pedido al contado no puede llevar «Mantener junto». El contado se cobra en cada
    /// entrega (contra reembolso), pero «Mantener junto» retiene la factura hasta completar el pedido: el primer envío
    /// salía con reembolso 0 (no había factura) y el que completaba solo cobraba su albarán, así que lo de los albaranes
    /// anteriores se quedaba sin cobrar (297,60 € en el 928220).
    /// <para>«Al contado» = plazos CONTADO o CR (los únicos que los triggers trgCabPedidoVtaIns/Upd no fuerzan a
    /// MantenerJunto = 1) y que se cobre contra reembolso con la regla de siempre (<see cref="GestorPortes.EsContraReembolso"/>:
    /// sin CCC, sin fin de mes, sin transferencia/tarjeta/CNF/CHC, sin nota de entrega, sin prepago). Un contado por
    /// transferencia o con tarjeta no se cobra en la entrega, así que «Mantener junto» no le hace daño.</para>
    /// <para>Pedidos que YA estaban así (35 en los últimos seis meses): un PUT que no lo activa no se rechaza, para no
    /// romper la edición; su reembolso ya cobra todo lo pendiente en el envío que completa el pedido
    /// (<see cref="Agencias.GestorEnviosAgencia.ImporteReembolso(CabPedidoVta, System.Collections.Generic.IEnumerable{LinPedidoVta}, IServicioPedidosVenta)"/>).</para>
    /// </summary>
    public static class MantenerJuntoContado
    {
        public const string MENSAJE = "Un pedido al contado no puede llevar «Mantener junto»: se cobra en cada entrega.";

        /// <summary>Lo que se avisa al hacer la etiqueta de un pedido que YA estaba así y aún tiene líneas pendientes.</summary>
        public const string AVISO_ENVIO_SIN_REEMBOLSO = "Este pedido es al contado y lleva «Mantener junto»: este envío sale sin reembolso " +
            "porque aún quedan líneas pendientes y no se factura hasta completarlo. Lo de este envío se cobrará con el envío que complete el " +
            "pedido. Si prefiere cobrarlo ahora, quite «Mantener junto» del pedido antes de hacer la etiqueta.";

        public static bool EsContado(string formaPago, string plazosPago, string ccc, string periodoFacturacion, bool notaEntrega)
        {
            string forma = formaPago?.Trim();
            string plazos = plazosPago?.Trim();
            string cuenta = string.IsNullOrWhiteSpace(ccc) ? null : ccc;
            string periodo = periodoFacturacion?.Trim();
            // Como el PUT: EFC no lleva CCC (FormasPago.CCCObligatorio = 0), aunque la pantalla lo mande.
            if (forma == Constantes.FormasPago.EFECTIVO)
            {
                cuenta = null;
            }
            bool plazosDeContado = string.Equals(plazos, Constantes.PlazosPago.CONTADO, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(plazos, Constantes.PlazosPago.CONTADO_RIGUROSO, StringComparison.OrdinalIgnoreCase);
            return plazosDeContado && GestorPortes.EsContraReembolso(forma, plazos, cuenta, periodo, notaEntrega);
        }

        public static bool EsContado(PedidoVentaDTO pedido)
            => pedido != null && EsContado(pedido.formaPago, pedido.plazosPago, pedido.ccc, pedido.periodoFacturacion, pedido.notaEntrega);

        public static bool EsContado(CabPedidoVta cabecera)
            => cabecera != null && EsContado(cabecera.Forma_Pago, cabecera.PlazosPago, cabecera.CCC, cabecera.Periodo_Facturacion, cabecera.NotaEntrega);

        /// <summary>
        /// POST: null si se puede crear, o <see cref="MENSAJE"/>. Se mira después de normalizar el modo de facturación
        /// (mantenerJunto ya es el derivado del modo).
        /// </summary>
        public static string MotivoRechazoAlCrear(PedidoVentaDTO pedido)
            => pedido != null && pedido.mantenerJunto && EsContado(pedido) ? MENSAJE : null;

        /// <summary>
        /// PUT: solo se rechaza al ENTRAR en «contado con Mantener junto» (marcar la casilla en un contado, o pasar a
        /// contado un pedido que la tiene). Si el pedido ya estaba así, se respeta: un PUT que no toca ni lo uno ni lo
        /// otro no puede romperse por esto.
        /// </summary>
        public static string MotivoRechazoAlModificar(PedidoVentaDTO pedido, CabPedidoVta anterior)
        {
            if (MotivoRechazoAlCrear(pedido) == null)
            {
                return null;
            }
            bool yaEstabaAsi = anterior != null && anterior.MantenerJunto && EsContado(anterior);
            return yaEstabaAsi ? null : MENSAJE;
        }
    }
}
