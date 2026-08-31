using System.Collections.Generic;

namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// NestoAPI#436: lo que un cliente final puede decir al crear su pedido desde la app.
    ///
    /// <para>La regla que ordena el diseño: <b>el cliente dice qué y cuánto; todo lo demás lo
    /// decide el servidor</b>. Por eso aquí no hay precios, ni descuentos, ni portes, ni almacén,
    /// ni vendedor, ni cliente: el cliente sale del JWT y el resto se calcula. Si algo de eso
    /// llegara en la petición, se ignora — no está ni siquiera en el contrato.</para>
    /// </summary>
    public class PedidoClienteRequest
    {
        public PedidoClienteRequest()
        {
            Lineas = new List<LineaPedidoClienteRequest>();
            PagarConTarjeta = true;
        }

        public ICollection<LineaPedidoClienteRequest> Lineas { get; set; }

        /// <summary>Comentario del cliente para el pedido (opcional).</summary>
        public string Comentarios { get; set; }

        /// <summary>Su referencia de pedido (opcional).</summary>
        public string SuPedido { get; set; }

        /// <summary>
        /// Por defecto, tarjeta al contado. Solo si el cliente pide otra cosa y su ficha se lo
        /// permite se usan <see cref="FormaPago"/> y <see cref="PlazosPago"/>.
        /// </summary>
        public bool PagarConTarjeta { get; set; }

        /// <summary>
        /// Forma de pago solicitada (opcional). Solo se respeta si la política del canal la
        /// autoriza para este cliente; si no, se usa la recomendada (tarjeta).
        /// </summary>
        public string FormaPago { get; set; }

        /// <summary>
        /// Plazos de pago solicitados (opcional), con el mismo criterio que <see cref="FormaPago"/>.
        /// </summary>
        public string PlazosPago { get; set; }
    }

    public class LineaPedidoClienteRequest
    {
        public string Producto { get; set; }
        public short Cantidad { get; set; }
    }

    /// <summary>
    /// NestoAPI#436: lo que la app necesita saber del pedido que se acaba de crear.
    /// </summary>
    public class PedidoClienteResponse
    {
        public PedidoClienteResponse()
        {
            Lineas = new List<LineaPedidoClienteResponse>();
            Avisos = new List<string>();
        }

        public string Empresa { get; set; }
        public int Numero { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string FormaPago { get; set; }
        public string PlazosPago { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal Total { get; set; }

        /// <summary>
        /// Portes que ha calculado el servidor (0 si el pedido supera el mínimo). Va aparte para
        /// que la app pueda explicárselo al cliente sin tener que adivinar qué línea son.
        /// </summary>
        public decimal Portes { get; set; }

        /// <summary>
        /// El pedido se cobra en el momento (tarjeta o prepago): la app tiene que llevar al
        /// cliente a la pasarela. Hasta que el cobro llegue, el pedido no coge picking.
        /// </summary>
        public bool RequierePago { get; set; }

        /// <summary>
        /// NestoAPI#436: los parámetros de Redsys con los que la app abre la pasarela, ya firmados
        /// y por el importe del pedido. Van aquí, y no en una llamada aparte a <c>api/Pagos</c>,
        /// porque el importe lo tiene que decir el servidor: si lo dijera el cliente, podría pagar
        /// 1 € por un pedido de 100. Es <c>null</c> cuando el pedido no se cobra en el momento, y
        /// también si el cobro no se pudo arrancar (ahí lo dicen los <see cref="Avisos"/>).
        /// </summary>
        public Pagos.RespuestaIniciarPago Pago { get; set; }

        public ICollection<LineaPedidoClienteResponse> Lineas { get; set; }

        /// <summary>
        /// Avisos para el cliente: sobre todo, que el pedido se ha quedado esperando revisión.
        /// Un pedido parado sin decir nada es peor que un error.
        /// </summary>
        public ICollection<string> Avisos { get; set; }
    }

    /// <summary>
    /// NestoAPI#436: lo que cuesta el envío del carrito, antes de crear el pedido. Lo pide la app
    /// para enseñar los gastos de envío y, sobre todo, el aviso de "te faltan X € para el envío
    /// gratis", que es la parte que sube el importe medio del pedido.
    /// </summary>
    public class PortesClienteResponse
    {
        /// <summary>Base imponible de los productos del carrito, con los precios del servidor.</summary>
        public decimal BaseImponibleProductos { get; set; }

        /// <summary>Lo que se le cobraría de portes hoy por hoy (0 si ya son gratis).</summary>
        public decimal Portes { get; set; }

        public bool PortesGratis { get; set; }

        /// <summary>A partir de este importe de productos no se cobran portes.</summary>
        public decimal ImporteMinimoSinPortes { get; set; }

        /// <summary>Lo que le falta al carrito para llegar al envío gratis (0 si ya llega).</summary>
        public decimal FaltaParaPortesGratis { get; set; }
    }

    public class LineaPedidoClienteResponse
    {
        public string Producto { get; set; }
        public string Texto { get; set; }
        public short Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal Descuento { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal Total { get; set; }
    }
}
