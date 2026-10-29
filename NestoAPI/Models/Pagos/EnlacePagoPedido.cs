using System;

namespace NestoAPI.Models.Pagos
{
    /// <summary>
    /// NestoAPI#679: un enlace de pago (PagosTPV de tipo TPV virtual) de un pedido. El enlace no guarda el pedido en
    /// ninguna columna: se sabe por el concepto («Pago pedido 928521 de Nueva Visión» desde Nesto, «Pedido 928521» desde
    /// NestoApp). Ver <see cref="Infraestructure.Pagos.EnlacesPagoPedido"/>.
    /// </summary>
    public class EnlacePagoPedido
    {
        public int Id { get; set; }
        public decimal Importe { get; set; }
        /// <summary>Uno de <see cref="Constantes.EstadosPagoTPV"/> (aquí solo llegan Pendiente y Autorizado).</summary>
        public string Estado { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaActualizacion { get; set; }
        public string Usuario { get; set; }
        /// <summary>NestoAPI#734: el concepto del enlace («Pago pedido 928521 de Nueva Visión»), para que Administración sepa de qué es.</summary>
        public string Descripcion { get; set; }
    }

    /// <summary>NestoAPI#679: un aviso al crear un enlace de pago (no bloquea: el enlace ya está creado).</summary>
    public class AvisoEnlacePago
    {
        /// <summary>Uno de <see cref="CodigosAvisoEnlacePago"/>.</summary>
        public string Codigo { get; set; }
        public string Texto { get; set; }
    }

    /// <summary>NestoAPI#679: los códigos de <see cref="AvisoEnlacePago"/>.</summary>
    public static class CodigosAvisoEnlacePago
    {
        /// <summary>El pedido va contra reembolso y aún no ha salido: se recomienda pasarlo a tarjeta prepago.</summary>
        public const string PEDIDO_CONTRA_REEMBOLSO = "PedidoContraReembolso";
        /// <summary>El pedido ya va en camino con un reembolso: si paga por enlace, la agencia intentará cobrarle otra vez.</summary>
        public const string ENVIO_EN_CAMINO_CON_REEMBOLSO = "EnvioEnCaminoConReembolso";
    }
}
