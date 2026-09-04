using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;

namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// TNV#66: un pedido del cliente tal y como lo ve él en la app, no como lo vemos nosotros.
    ///
    /// <para>El cliente no distingue pedido de albarán ni de factura, así que aquí no viajan esos
    /// estados: viaja <see cref="Estado"/>, que es lo que le pasa a su compra. Y no viaja tampoco
    /// el detalle completo del pedido: con el número, la fecha, el importe, qué lleva y por dónde
    /// va tiene lo que necesita para saber que su pedido existe y está en marcha.</para>
    /// </summary>
    public class PedidoClienteResumenDTO
    {
        public int Numero { get; set; }
        public DateTime? Fecha { get; set; }

        /// <summary>Lo que cuesta el pedido con IVA, portes incluidos.</summary>
        public decimal Total { get; set; }

        /// <summary>Unidades de producto (los textos y las líneas de portes no cuentan).</summary>
        public int NumeroArticulos { get; set; }

        /// <summary>
        /// Los primeros productos, para que reconozca el pedido de un vistazo sin tener que
        /// abrirlo. No es el detalle: es la etiqueta de la caja.
        /// </summary>
        public List<string> Articulos { get; set; } = new List<string>();

        /// <summary>
        /// Ver <see cref="EstadoPedidoCliente"/>. Viaja como TEXTO ("Enviado"), no como número:
        /// un cliente que lee "3" depende de que nadie reordene el enum, y son tres clientes.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public EstadoPedidoCliente Estado { get; set; }

        /// <summary>El mismo estado, ya escrito para enseñárselo tal cual.</summary>
        public string EstadoTexto { get; set; }

        /// <summary>
        /// Está esperando su dinero: es prepago y lo cobrado no llega al total. Es la diferencia
        /// que no puede pasar desapercibida entre un pedido pagado con tarjeta y uno pendiente de
        /// transferencia, o el cliente creerá que ya está todo hecho.
        /// </summary>
        public bool PendienteDePago { get; set; }

        /// <summary>Lo que falta por cobrar (0 si no hay nada pendiente).</summary>
        public decimal ImportePendiente { get; set; }

        public string FormaPago { get; set; }
        public string PlazosPago { get; set; }

        /// <summary>
        /// El envío, cuando ya ha salido, con su seguimiento. Es el mismo DTO que devuelve
        /// <c>EnviosAgencias/UltimoEnvioCliente</c>: misma URL de seguimiento y mismo contrato,
        /// para que la app no tenga dos formas de pintar lo mismo (TNV#5).
        /// </summary>
        public UltimoEnvioClienteDTO Envio { get; set; }
    }

    /// <summary>
    /// TNV#66: en qué punto está la compra, traducido a lo que el cliente entiende. El orden es
    /// el del recorrido, de lo más al principio a lo más terminado.
    /// </summary>
    public enum EstadoPedidoCliente
    {
        /// <summary>Esperando su pago: hasta que no entre, el pedido no se prepara.</summary>
        PendienteDePago,

        /// <summary>
        /// Lo tenemos y todavía no se ha movido: en curso sin picking, o esperando existencias
        /// (líneas en PENDIENTE, a la espera de una tienda o de un proveedor).
        /// </summary>
        Recibido,

        /// <summary>El almacén lo está montando: líneas EN_CURSO con picking asignado.</summary>
        EnPreparacion,

        /// <summary>
        /// Parte del pedido ya está entregada a la agencia y parte no. Se le dice: si no, abre
        /// una caja incompleta creyendo que le falta algo.
        /// </summary>
        EnviadoEnParte,

        /// <summary>Todo entregado a la agencia y de camino.</summary>
        Enviado,

        /// <summary>
        /// La agencia no ha podido entregarlo. Es el único estado malo que el cliente SÍ puede
        /// resolver (llamar, dar otra dirección), así que se le dice.
        /// </summary>
        Incidencia,

        /// <summary>La agencia lo ha entregado (lo dice su seguimiento, no nosotros).</summary>
        Entregado,

        /// <summary>Servido entero sin envío por agencia (recogida en tienda, ruta propia).</summary>
        Servido
    }
}
