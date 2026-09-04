using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// TNV#66: traduce un pedido de nuestro modelo (líneas, estados, albaranes, prepagos) a lo
    /// que el cliente entiende: «lo tenemos», «lo estamos preparando», «ha salido», «entregado».
    ///
    /// <para>Está fuera del controller a propósito: son las reglas que deciden lo que lee el
    /// cliente después de pagar, y esas se prueban una a una sin base de datos.</para>
    /// </summary>
    internal static class ResumidorPedidosCliente
    {
        /// <summary>
        /// Margen con el que se da un prepago por suficiente, el mismo que usa el picking para
        /// soltar el pedido (PedidoPicking.DESCUADRE_PERMITIDO). Si aquí fuéramos más estrictos,
        /// le diríamos «pendiente de pago» a un pedido que el almacén ya está preparando.
        /// </summary>
        internal const decimal DESCUADRE_PERMITIDO = .25M;

        /// <summary>Cuántos productos se nombran en el resumen antes de resumir en «y N más».</summary>
        internal const int ARTICULOS_QUE_SE_NOMBRAN = 3;

        internal static PedidoClienteResumenDTO Resumir(DatosPedidoCliente pedido)
        {
            List<DatosLineaPedidoCliente> lineas = pedido.Lineas ?? new List<DatosLineaPedidoCliente>();
            List<DatosLineaPedidoCliente> productos = lineas
                .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                .ToList();

            decimal total = decimal.Round(lineas.Sum(l => l.Total), 2, MidpointRounding.AwayFromZero);
            decimal pendiente = ImportePendienteDePago(
                pedido.PlazosPago, total, pedido.ImportePrepagado, SigueEsperandoSalir(lineas));
            EstadoPedidoCliente estado = CalcularEstado(pedido, lineas, pendiente > 0m);

            return new PedidoClienteResumenDTO
            {
                Numero = pedido.Numero,
                Fecha = pedido.Fecha,
                Total = total,
                NumeroArticulos = productos.Sum(l => (int)(l.Cantidad ?? 0)),
                Articulos = productos
                    .Select(l => l.Texto?.Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Take(ARTICULOS_QUE_SE_NOMBRAN)
                    .ToList(),
                Estado = estado,
                EstadoTexto = TextoDe(estado),
                PendienteDePago = pendiente > 0m,
                ImportePendiente = pendiente,
                FormaPago = pedido.FormaPago?.Trim(),
                PlazosPago = pedido.PlazosPago?.Trim(),
                Envio = pedido.Envio
            };
        }

        /// <summary>
        /// Lo que falta por cobrar de un pedido de prepago. En los demás (recibo, transferencia a
        /// vencimiento) el pedido no espera al dinero, así que no se le dice nada al cliente.
        ///
        /// <para>Solo se miran los prepagos del pedido, no el saldo a favor del cliente: el picking
        /// sí lo cuenta (PedidoPicking.saleEnPicking), pero decirle a alguien que su pedido está
        /// pagado porque tiene un abono suelto sería adivinar por él.</para>
        /// </summary>
        internal static decimal ImportePendienteDePago(
            string plazosPago, decimal total, decimal importePrepagado, bool sigueEsperandoSalir)
        {
            if (plazosPago?.Trim() != Constantes.PlazosPago.PREPAGO)
            {
                return 0m;
            }

            // Un pedido ya servido no espera dinero: al facturarlo, su prepago se enlaza a la
            // factura y deja de contar aquí (Prepagos.Factura), asi que sin esta salida TODOS los
            // pedidos de tarjeta ya facturados —que estan cobrados— saldrian como pendientes de
            // pago. A partir de ahi el cobro vive en el extracto del cliente, no en el pedido.
            if (!sigueEsperandoSalir)
            {
                return 0m;
            }

            decimal falta = total - importePrepagado;
            return falta > DESCUADRE_PERMITIDO ? decimal.Round(falta, 2, MidpointRounding.AwayFromZero) : 0m;
        }

        /// <summary>
        /// Le queda algo por salir del almacén: alguna línea viva sin albaranar. Es lo que
        /// distingue un pedido que todavía espera de uno que ya se sirvió.
        /// </summary>
        internal static bool SigueEsperandoSalir(List<DatosLineaPedidoCliente> lineas)
        {
            return lineas.Any(l => l.Estado > Constantes.EstadosLineaVenta.PRESUPUESTO
                                   && l.Estado < Constantes.EstadosLineaVenta.ALBARAN);
        }

        /// <summary>
        /// El punto del recorrido en el que está, según los estados de las líneas:
        ///
        /// <list type="bullet">
        /// <item>Alguna línea en ALBARAN (2) o más, pero no todas: parte del pedido ya está en la
        /// agencia y parte no. Se dice, porque si no el cliente abre una caja incompleta y cree
        /// que le falta algo.</item>
        /// <item>Todas en ALBARAN o más: entregado a la agencia. A partir de ahí, que esté
        /// entregado AL CLIENTE lo dice el seguimiento, no nosotros.</item>
        /// <item>Alguna en EN_CURSO (1) con picking asignado: el almacén lo está montando.</item>
        /// <item>El resto (EN_CURSO sin picking, o PENDIENTE (-1) esperando existencias): lo
        /// tenemos, todavía no se ha movido.</item>
        /// </list>
        ///
        /// <para>Lo que ya ha pasado de verdad manda sobre lo que está esperando: un pedido que ha
        /// salido está enviado aunque quede algo por cobrar (no debería pasar —el picking lo
        /// retiene—, pero si pasa, negarle el envío sería mentirle).</para>
        /// </summary>
        internal static EstadoPedidoCliente CalcularEstado(
            DatosPedidoCliente pedido, List<DatosLineaPedidoCliente> lineas, bool pendienteDePago)
        {
            List<DatosLineaPedidoCliente> vivas = lineas
                .Where(l => l.Estado > Constantes.EstadosLineaVenta.PRESUPUESTO)
                .ToList();

            bool algoEntregadoALaAgencia = vivas.Any(l => l.Estado >= Constantes.EstadosLineaVenta.ALBARAN);
            bool todoEntregadoALaAgencia = vivas.Count > 0
                && vivas.All(l => l.Estado >= Constantes.EstadosLineaVenta.ALBARAN);

            if (todoEntregadoALaAgencia)
            {
                if (pedido.Envio == null)
                {
                    // Sin envío por agencia que seguir: recogida en tienda o ruta propia.
                    return EstadoPedidoCliente.Servido;
                }
                bool entregado = pedido.Envio.FechaEntrega.HasValue
                    || pedido.Envio.Estado == Constantes.Agencias.ESTADO_ENTREGADO;
                if (entregado)
                {
                    return EstadoPedidoCliente.Entregado;
                }
                // La agencia no ha podido entregarlo: es lo único que el cliente puede resolver
                // (llamar, dar otra dirección), así que no se le esconde detrás de "en camino".
                return pedido.Envio.Estado == Constantes.Agencias.ESTADO_INCIDENTADO
                    ? EstadoPedidoCliente.Incidencia
                    : EstadoPedidoCliente.Enviado;
            }

            if (algoEntregadoALaAgencia)
            {
                return EstadoPedidoCliente.EnviadoEnParte;
            }

            if (pendienteDePago)
            {
                return EstadoPedidoCliente.PendienteDePago;
            }

            // Estado 1 a secas solo significa que el pedido está en curso: lo que dice que el
            // almacén lo está montando es tener picking asignado (así nacen los pedidos de la
            // app, en EN_CURSO y sin picking).
            bool enPicking = vivas.Any(l => l.Estado == Constantes.EstadosLineaVenta.EN_CURSO
                                            && l.Picking.HasValue && l.Picking.Value != 0);

            return enPicking ? EstadoPedidoCliente.EnPreparacion : EstadoPedidoCliente.Recibido;
        }

        internal static string TextoDe(EstadoPedidoCliente estado)
        {
            switch (estado)
            {
                case EstadoPedidoCliente.PendienteDePago:
                    return "Pendiente de pago";
                case EstadoPedidoCliente.Recibido:
                    return "Lo hemos recibido";
                case EstadoPedidoCliente.EnPreparacion:
                    return "Preparándolo";
                case EstadoPedidoCliente.EnviadoEnParte:
                    return "Una parte va en camino";
                case EstadoPedidoCliente.Enviado:
                    return "En camino";
                case EstadoPedidoCliente.Incidencia:
                    return "Incidencia con la entrega";
                case EstadoPedidoCliente.Entregado:
                    return "Entregado";
                case EstadoPedidoCliente.Servido:
                    return "Servido";
                default:
                    return string.Empty;
            }
        }
    }

    /// <summary>TNV#66: lo que hace falta saber de un pedido para resumírselo al cliente.</summary>
    internal class DatosPedidoCliente
    {
        public int Numero { get; set; }
        public DateTime? Fecha { get; set; }
        public string FormaPago { get; set; }
        public string PlazosPago { get; set; }
        public decimal ImportePrepagado { get; set; }
        public UltimoEnvioClienteDTO Envio { get; set; }
        public List<DatosLineaPedidoCliente> Lineas { get; set; } = new List<DatosLineaPedidoCliente>();
    }

    internal class DatosLineaPedidoCliente
    {
        public short Estado { get; set; }

        /// <summary>
        /// Número de picking. Con estado EN_CURSO, es lo que distingue un pedido que el almacén
        /// está montando de uno que solo está aceptado. Null o 0 = todavía nadie lo ha cogido.
        /// </summary>
        public int? Picking { get; set; }
        public byte? TipoLinea { get; set; }
        public short? Cantidad { get; set; }
        public decimal Total { get; set; }
        public string Texto { get; set; }
    }
}
