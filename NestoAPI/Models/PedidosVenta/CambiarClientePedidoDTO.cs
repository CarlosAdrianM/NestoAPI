using System.Collections.Generic;

namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// NestoAPI#519: cuerpo de <c>POST api/PedidosVenta/{empresa}/{numero}/CambiarCliente</c>.
    /// </summary>
    public class CambiarClientePedidoRequest
    {
        /// <summary>El cliente al que pasa el pedido.</summary>
        public string Cliente { get; set; }

        /// <summary>
        /// El contacto (dirección de entrega) del cliente nuevo. Vacío = su contacto principal.
        /// </summary>
        public string Contacto { get; set; }

        /// <summary>Quién hace el cambio (si no viene, el del token).</summary>
        public string Usuario { get; set; }

        /// <summary>
        /// Igual que en el PUT: el usuario ya ha visto que el pedido no pasa la validación con el cliente
        /// nuevo y quiere seguir. Solo vale para quien puede crear pedidos con errores (Dirección, Almacén o
        /// el parámetro PermitirCrearPedidoConErrores) y la denegación no exige un permiso propio.
        /// </summary>
        public bool CreadoSinPasarValidacion { get; set; }
    }

    /// <summary>NestoAPI#519: lo que ha pasado al cambiar el cliente, para enseñárselo al usuario.</summary>
    public class CambiarClientePedidoRespuesta
    {
        public string Empresa { get; set; }
        public int Numero { get; set; }
        public string ClienteAnterior { get; set; }
        public string ContactoAnterior { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }

        /// <summary>Cada cosa que ha cambiado, en lenguaje de usuario («Forma de pago: RCB → EFC»).</summary>
        public List<string> Cambios { get; set; } = new List<string>();
    }
}
