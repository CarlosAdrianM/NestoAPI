using System;

namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// Nesto#550 / Nesto#554 (sugerencias 759 y 775, Paloma): un pedido pendiente del cliente tal y como lo enseña la
    /// plantilla de venta («Ampliar en el pedido» y el diálogo «Pedidos pendientes»). Sale de
    /// GET api/PlantillaVentas/PedidosPendientesDetalle. El GET PedidosPendientes de siempre (solo números) se mantiene
    /// para NestoApp.
    /// </summary>
    public class PedidoPendientePlantillaDTO
    {
        public int Numero { get; set; }
        /// <summary>Fecha del pedido (cabecera).</summary>
        public DateTime? Fecha { get; set; }
        /// <summary>Líneas pendientes (estado entre -1 y 1).</summary>
        public int NumeroLineas { get; set; }
        /// <summary>Suma de la base imponible de las líneas pendientes.</summary>
        public decimal BaseImponible { get; set; }
        /// <summary>La fecha de entrega más próxima de las líneas pendientes.</summary>
        public DateTime? FechaEntrega { get; set; }
        /// <summary>Nesto#554: las notas de entrega no se pueden modificar desde la plantilla.</summary>
        public bool NotaEntrega { get; set; }
        /// <summary>Alguna línea pendiente ya tiene picking.</summary>
        public bool TienePicking { get; set; }
        /// <summary>
        /// Retenido «hasta nuevo aviso» (NestoAPI#698). Mientras #698 no tenga su tabla, se deduce de la costumbre que
        /// describe: la fecha de entrega más próxima está a más de
        /// <see cref="Constantes.Pedidos.DIAS_AVISO_FECHA_ENTREGA_LEJANA"/> días (fechas al año que viene, 2099...).
        /// </summary>
        public bool Retenido { get; set; }
    }
}
