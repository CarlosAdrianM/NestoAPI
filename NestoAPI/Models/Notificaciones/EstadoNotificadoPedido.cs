using System;

namespace NestoAPI.Models.Notificaciones
{
    /// <summary>
    /// TNV#66: una fila de <c>dbo.NotificacionesEstadoPedido</c>: qué estado se le vio a un pedido
    /// en la última pasada del job y de qué estado se avisó al cliente.
    ///
    /// <para>La tabla NO está en el EDMX: se lee y escribe con SQL crudo desde
    /// <see cref="Infraestructure.Notificaciones.AlmacenEstadoNotificadoPedido"/>, mismo patrón
    /// que AmazonFacturasSubidas (#366).</para>
    /// </summary>
    public class EstadoNotificadoPedido
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }

        /// <summary>Estado visto en la última pasada (nombre de <c>EstadoPedidoCliente</c>).</summary>
        public string Estado { get; set; }

        /// <summary>Desde cuándo está en ese estado. Es lo que permite avisar por tiempo.</summary>
        public DateTime FechaEstado { get; set; }

        /// <summary>Último estado del que se avisó. Null = a este pedido no se le ha avisado nunca.</summary>
        public string EstadoNotificado { get; set; }

        public DateTime? FechaNotificacion { get; set; }
    }
}
