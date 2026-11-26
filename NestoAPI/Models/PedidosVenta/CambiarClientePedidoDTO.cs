using System.Collections.Generic;

namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>NestoAPI#582: cuerpo de <c>POST api/PedidosVenta/{empresa}/{nota}/FechaEntrega</c>.</summary>
    /// <summary>NestoAPI#688: marcar o desmarcar en una nota «¿Servir lo pendiente en cuanto haya stock?».</summary>
    public class ServirPendienteAlHaberStockRequest
    {
        public bool Valor { get; set; }

        /// <summary>Solo para auditoría si no viene del token.</summary>
        public string Usuario { get; set; }
    }

    public class FechaEntregaNotaRequest
    {
        public System.DateTime FechaEntrega { get; set; }

        /// <summary>Solo para auditoría si no viene del token.</summary>
        public string Usuario { get; set; }
    }

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

        /// <summary>
        /// NestoAPI#876: solo calcular («en seco») qué cambiaría, sin guardar nada. Los portes no se calculan en el ensayo
        /// (los pone el guardado de verdad).
        /// </summary>
        public bool Ensayo { get; set; }
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

        /// <summary>NestoAPI#876: era un ensayo (no se ha guardado nada; <see cref="Cambios"/> es lo que cambiaría).</summary>
        public bool Ensayo { get; set; }

        /// <summary>NestoAPI#876: al recalcular, ninguna línea cambia de precio, descuentos, IVA ni importes y la cabecera no cambia de IVA.</summary>
        public bool SinCambiosDeImportes { get; set; }

        /// <summary>NestoAPI#876: el pedido tenía picking (se ha cambiado sin tocarlo, porque no cambiaba nada).</summary>
        public bool TeniaPicking { get; set; }

        /// <summary>NestoAPI#876: lo que conviene saber (portes que no se calculan en el ensayo, ruta que se queda con picking…).</summary>
        public List<string> Avisos { get; set; } = new List<string>();
    }
}
