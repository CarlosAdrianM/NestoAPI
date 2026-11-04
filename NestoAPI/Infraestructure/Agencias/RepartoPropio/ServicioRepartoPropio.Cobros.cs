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
    }
}
