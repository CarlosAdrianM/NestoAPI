using NestoAPI.Models;
using NestoAPI.Models.Notificaciones;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Notificaciones
{
    /// <summary>
    /// TNV#66: qué cambios de estado de un pedido se le avisan al cliente por push, y con qué
    /// texto.
    ///
    /// <para><b>La regla que ordena esto: solo se avisa de lo que el cliente no puede saber por su
    /// cuenta o puede resolver.</b> «Lo hemos recibido» ya lo sabe (acaba de darle a confirmar),
    /// «preparándolo» no le aporta nada y «entregado» se lo acaba de dar el repartidor en la mano.
    /// Avisar de todo es la forma más rápida de que apague las notificaciones, y entonces se
    /// pierden también las que sí importan.</para>
    ///
    /// <para>Salen 1 o 2 avisos por pedido. Lo que satura es el push promocional, no este.</para>
    /// </summary>
    internal static class AvisosEstadoPedido
    {
        /// <summary>
        /// Cuánto tiene que llevar un pedido esperando el pago antes de recordárselo. No se avisa
        /// al crearlo: el cliente acaba de ver la pantalla del pago y puede estar pagando en ese
        /// momento; perseguirle a los cinco minutos es de cobrador, no de tienda.
        /// </summary>
        internal static readonly TimeSpan EsperaAntesDeRecordarElPago = TimeSpan.FromHours(4);

        /// <summary>Tipo de la notificación, para que la app sepa a dónde llevar al tocarla.</summary>
        internal const string TIPO = "pedido";

        /// <summary>
        /// Estados que se avisan en cuanto se detectan, porque son novedades que el cliente no
        /// puede conocer de otra forma.
        /// </summary>
        private static readonly HashSet<EstadoPedidoCliente> AvisoInmediato = new HashSet<EstadoPedidoCliente>
        {
            EstadoPedidoCliente.Enviado,
            EstadoPedidoCliente.EnviadoEnParte,
            EstadoPedidoCliente.Incidencia
        };

        /// <summary>
        /// Decide si hay que avisar de este pedido ahora mismo, comparando con lo que se vio y se
        /// notificó la última vez. Es una función pura: el job solo la aplica.
        /// </summary>
        /// <param name="estado">Estado del pedido en esta pasada.</param>
        /// <param name="registro">Lo guardado de pasadas anteriores; null si es la primera vez.</param>
        /// <param name="ahora">Momento de la pasada.</param>
        internal static bool HayQueAvisar(EstadoPedidoCliente estado, EstadoNotificadoPedido registro, DateTime ahora)
        {
            // Primera vez que se ve el pedido: se guarda su estado y NO se avisa. Al desplegar
            // esto hay meses de pedidos, y nadie quiere estrenar la función con una avalancha de
            // avisos de envíos que llegaron hace semanas.
            if (registro == null)
            {
                return false;
            }

            // Ya se le avisó de este mismo estado: el job pasa cada media hora y el estado dura
            // días, así que sin esto sería el mismo aviso una y otra vez toda la semana.
            if (string.Equals(registro.EstadoNotificado, estado.ToString(), StringComparison.Ordinal))
            {
                return false;
            }

            if (AvisoInmediato.Contains(estado))
            {
                return true;
            }

            if (estado == EstadoPedidoCliente.PendienteDePago)
            {
                // Por tiempo, no por cambio: el pedido nace ya pendiente de pago, así que esperar
                // a "que cambie a pendiente de pago" no avisaría nunca.
                return ahora - registro.FechaEstado >= EsperaAntesDeRecordarElPago;
            }

            return false;
        }

        /// <summary>
        /// El aviso que se le manda. El cuerpo dice el número de pedido porque es lo que el
        /// cliente usa para hablar con nosotros por teléfono.
        /// </summary>
        internal static NotificacionPushDTO Construir(PedidoClienteResumenDTO pedido)
        {
            string titulo;
            string cuerpo;

            switch (pedido.Estado)
            {
                case EstadoPedidoCliente.Enviado:
                    titulo = "Tu pedido va en camino";
                    cuerpo = $"El pedido {pedido.Numero} ha salido{ConLaAgencia(pedido)}. " +
                             "Puedes seguirlo desde Mis pedidos.";
                    break;

                case EstadoPedidoCliente.EnviadoEnParte:
                    // El aviso que evita la llamada de "me falta media caja".
                    titulo = "Una parte de tu pedido va en camino";
                    cuerpo = $"Del pedido {pedido.Numero} ha salido una parte{ConLaAgencia(pedido)}; " +
                             "el resto te llegará en cuanto lo tengamos.";
                    break;

                case EstadoPedidoCliente.Incidencia:
                    titulo = "Incidencia con tu envío";
                    cuerpo = $"La agencia no ha podido entregarte el pedido {pedido.Numero}. " +
                             "Mira el seguimiento en Mis pedidos o llámanos y lo resolvemos.";
                    break;

                case EstadoPedidoCliente.PendienteDePago:
                    titulo = "Tu pedido espera el pago";
                    cuerpo = $"El pedido {pedido.Numero} no se prepara hasta que recibamos el pago" +
                             $"{ConElImporte(pedido)}. Puedes pagarlo desde la app.";
                    break;

                default:
                    return null;
            }

            return new NotificacionPushDTO
            {
                Titulo = titulo,
                Cuerpo = cuerpo,
                Tipo = TIPO,
                Datos = new Dictionary<string, string>
                {
                    { "tipo", TIPO },
                    { "pedido", pedido.Numero.ToString() },
                    { "estado", pedido.Estado.ToString() }
                }
            };
        }

        private static string ConLaAgencia(PedidoClienteResumenDTO pedido)
        {
            string agencia = pedido.Envio?.AgenciaNombre?.Trim();
            return string.IsNullOrWhiteSpace(agencia) ? string.Empty : $" con {agencia}";
        }

        private static string ConElImporte(PedidoClienteResumenDTO pedido)
        {
            // Sin importe cuando no lo hay o cuando el usuario no puede ver precios: el aviso vale
            // igual y no se le enseña una cifra que en su pantalla está oculta.
            return pedido.ImportePendiente > 0m ? $" ({pedido.ImportePendiente:N2} €)" : string.Empty;
        }
    }
}
