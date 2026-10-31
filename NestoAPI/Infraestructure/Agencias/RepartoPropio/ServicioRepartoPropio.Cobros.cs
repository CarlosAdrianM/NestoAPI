namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    public partial class ServicioRepartoPropio
    {
        /// <summary>
        /// NestoAPI#773: lo que se comprueba del cobro al entregar (tarjeta pagada, autorización para no cobrar). Null: como antes de
        /// #773 (sin tarjeta y sin exigir autorización). Lo pone el controlador.
        /// </summary>
        internal ComprobadorCobroEntrega CobroEntrega { get; set; }
    }
}
