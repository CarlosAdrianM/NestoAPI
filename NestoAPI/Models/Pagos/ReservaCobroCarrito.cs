namespace NestoAPI.Models.Pagos
{
    /// <summary>
    /// TNV#68: el resultado de tomar el cobro del carrito para el pedido que se va a crear.
    ///
    /// <para>Nace del pedido 925607 (07/09/26), que se creó aunque el cliente canceló el pago.
    /// Con el orden nuevo —cobrar primero, crear después— el pedido solo se crea si este cobro
    /// existe, está autorizado, es de este cliente y no se ha usado ya en otro pedido.</para>
    /// </summary>
    public class ReservaCobroCarrito
    {
        /// <summary>El cobro sirve para crear el pedido y queda reservado para él.</summary>
        public bool Valido { get; set; }

        /// <summary>
        /// Por qué no sirve, redactado para el cliente: es lo que va a leer en la app. Null
        /// cuando <see cref="Valido"/>.
        /// </summary>
        public string Motivo { get; set; }

        /// <summary>
        /// El cobro no sirve Y hay dinero que devolver (caducó sin llegar a ser pedido). En los
        /// demás "no" no hay nada que devolver: o no es nuestro, o el banco no lo autorizó, o ya
        /// tiene su pedido.
        /// </summary>
        public bool HayQueDevolver { get; set; }

        public int IdPago { get; set; }
        public string NumeroOrden { get; set; }

        /// <summary>Lo que se le ha cobrado de verdad, que es contra lo que se compara el total
        /// del pedido antes de crearlo.</summary>
        public decimal Importe { get; set; }

        public static ReservaCobroCarrito No(string motivo)
        {
            return new ReservaCobroCarrito { Valido = false, Motivo = motivo };
        }
    }
}
