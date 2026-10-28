using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Sugerencia 832 (Lidia, 28/10/26; OK de Carlos): la fecha de entrega a la agencia prometida se guarda al crear el pedido y
    /// no se toca... salvo que el usuario ADELANTE la fecha de entrega de un pedido que aún no tiene picking (el 928658 nació
    /// con entrega 01/01/2027 por error y la prometida se quedó ahí: nunca saltaría como «en riesgo»). Entonces la prometida
    /// pasa al nuevo cálculo si es anterior. Aplazar no la toca (eso es un retraso y lo detecta la promesa en riesgo).
    /// </summary>
    public static class AdelantoFechaEntrega
    {
        /// <summary>La primera fecha de entrega de las líneas vivas (pendientes o en curso); null si no hay.</summary>
        public static DateTime? FechaMinima(IEnumerable<LineaPedidoVentaDTO> lineas)
            => lineas?.Where(l => l != null && l.estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.estado <= Constantes.EstadosLineaVenta.EN_CURSO && l.Cantidad != 0)
                .Select(l => (DateTime?)l.fechaEntrega.Date).Min();

        public static DateTime? FechaMinima(IEnumerable<LinPedidoVta> lineas)
            => lineas?.Where(l => l != null && l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO && l.Cantidad != 0)
                .Select(l => (DateTime?)l.Fecha_Entrega.Date).Min();

        /// <summary>True si el pedido no tiene picking y la primera entrega queda ANTES que la que tenía.</summary>
        public static bool SeAdelanta(bool algunaLineaTienePicking, DateTime? antes, DateTime? despues)
            => !algunaLineaTienePicking && antes.HasValue && despues.HasValue && despues.Value.Date < antes.Value.Date;
    }
}
