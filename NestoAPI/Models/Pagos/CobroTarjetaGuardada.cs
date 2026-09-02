namespace NestoAPI.Models.Pagos
{
    /// <summary>
    /// NestoAPI#178/#181: cobro directo con una tarjeta guardada (token de Redsys). El importe
    /// y el cliente los pone SIEMPRE el servidor: esta clase no viaja por la API.
    /// </summary>
    public class SolicitudCobroTarjetaGuardada
    {
        public string Empresa { get; set; } = Constantes.Empresas.EMPRESA_POR_DEFECTO;
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public decimal Importe { get; set; }
        public string Descripcion { get; set; }

        /// <summary>Id en TarjetasClientes. El servidor comprueba que es del cliente.</summary>
        public int TarjetaId { get; set; }
    }

    /// <summary>
    /// NestoAPI#178: alta de tarjeta sin cobro (autorización de 0 EUR en la pasarela). El
    /// cliente y el correo los pone el servidor desde el JWT; la app solo puede decir a dónde
    /// volver (UrlOk/UrlKo).
    /// </summary>
    public class SolicitudAltaTarjeta
    {
        public string Empresa { get; set; } = Constantes.Empresas.EMPRESA_POR_DEFECTO;
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Correo { get; set; }
        public string UrlOk { get; set; }
        public string UrlKo { get; set; }
    }

    /// <summary>
    /// El resultado del cobro, síncrono: o está autorizado o no, no hay estado intermedio.
    /// </summary>
    public class ResultadoCobroTarjetaGuardada
    {
        public bool Autorizado { get; set; }
        public int IdPago { get; set; }
        public string NumeroOrden { get; set; }
        public string CodigoRespuesta { get; set; }

        /// <summary>Para poder decirle al cliente en qué tarjeta se ha cobrado. Puede faltar.</summary>
        public string UltimosDigitos { get; set; }

        /// <summary>El nombre de la tarjeta para el cliente (<see cref="TarjetaCliente.Describir"/>).</summary>
        public string Descripcion { get; set; }

        /// <summary>Motivo (para el cliente) cuando no se autoriza.</summary>
        public string MensajeError { get; set; }
    }
}
