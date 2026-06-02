namespace NestoAPI.Models
{
    /// <summary>
    /// Error no controlado reportado por una aplicación cliente (Nesto, NestoApp, TiendasNuevaVision)
    /// para que quede registrado de forma centralizada en ELMAH.
    /// </summary>
    public class ErrorClienteDTO
    {
        /// <summary>Aplicación que reporta el error (ej: "Nesto").</summary>
        public string Aplicacion { get; set; }

        /// <summary>Versión de la aplicación cliente.</summary>
        public string Version { get; set; }

        /// <summary>Nombre del tipo de excepción (ej: "NullReferenceException").</summary>
        public string TipoExcepcion { get; set; }

        /// <summary>Mensaje de la excepción.</summary>
        public string Mensaje { get; set; }

        /// <summary>Pila de llamadas (stack trace) capturada en el cliente.</summary>
        public string StackTrace { get; set; }

        /// <summary>Contexto opcional donde se produjo (ventana, acción, comando...).</summary>
        public string Contexto { get; set; }

        /// <summary>
        /// Usuario conocido por el cliente. Útil cuando no hay token válido (pre-login,
        /// token caducado): si la petición está autenticada, el usuario real sale del Identity.
        /// </summary>
        public string UsuarioCliente { get; set; }

        /// <summary>Plataforma del cliente (ej: "Windows", "Android", "iOS", "Web").</summary>
        public string Plataforma { get; set; }
    }
}
