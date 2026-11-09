namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    public partial class ServicioRepartoPropio
    {
        /// <summary>
        /// NestoAPI#773: lo que se comprueba del cobro al entregar (tarjeta pagada, autorización para no cobrar). Null: como antes de
        /// #773 (sin tarjeta y sin exigir autorización). Lo pone el controlador.
        /// </summary>
        internal ComprobadorCobroEntrega CobroEntrega { get; set; }

        /// <summary>
        /// NestoAPI#786: el cobro en efectivo de una nota de cobro al entregarla, con la regla de «Cobrar una deuda» (#766): liquida sus
        /// efectos en su orden y lo que sobra va a cuenta. Null: se contabiliza como el reembolso de un envío (#807). Lo pone el controlador.
        /// </summary>
        internal ServicioCobrosReparto CobrosDeuda { get; set; }

        /// <summary>
        /// NestoAPI#844: la caja sin cerrar (no se termina la jornada con dinero en la caja; no se empieza el día con dinero de días
        /// anteriores). Null: no se mira (como antes de #844). Lo pone el controlador.
        /// </summary>
        internal ControlCajaReparto ControlCaja { get; set; }
    }
}
