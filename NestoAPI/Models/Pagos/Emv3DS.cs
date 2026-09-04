using Newtonsoft.Json;

namespace NestoAPI.Models.Pagos
{
    /// <summary>
    /// NestoAPI#181: datos del navegador que EMV 3DS 2 manda al emisor para que decida si puede
    /// autenticar sin preguntar nada al cliente (frictionless). Cuantos más datos reales van,
    /// menos desafíos salen: por eso los recoge la página que se abre en el WebView de la app,
    /// en vez de inventárnoslos en el servidor.
    /// </summary>
    public class DatosNavegador3DS
    {
        public string UserAgent { get; set; }
        public string AcceptHeader { get; set; }
        public string Idioma { get; set; }
        public int ProfundidadColor { get; set; }
        public int AltoPantalla { get; set; }
        public int AnchoPantalla { get; set; }

        /// <summary>Diferencia con UTC en minutos, tal cual la da getTimezoneOffset().</summary>
        public int DiferenciaHorariaMinutos { get; set; }

        public bool JavaScriptActivado { get; set; }
    }

    /// <summary>
    /// NestoAPI#181: lo que devuelve el primer paso (iniciaPeticionREST con threeDSInfo=CardData).
    /// </summary>
    public class InicioAutenticacion3DS
    {
        /// <summary>
        /// False cuando la tarjeta no soporta EMV 3DS 2 (Redsys responde
        /// <c>protocolVersion = "NO_3DS_v2"</c>). En ese caso hay que caer al flujo por
        /// redirección de siempre, que resuelve 3DS 1 por su cuenta.
        /// </summary>
        public bool Soporta3DS2 { get; set; }

        public string ProtocolVersion { get; set; }
        public string ThreeDSServerTransID { get; set; }

        /// <summary>
        /// URL del emisor a la que hay que hacer POST en un iframe oculto para que recoja datos
        /// del dispositivo. Puede venir vacía: entonces no hay 3DSMethod y se pasa directamente
        /// a la autenticación.
        /// </summary>
        public string ThreeDSMethodURL { get; set; }

        /// <summary>
        /// El campo que hay que postear a <see cref="ThreeDSMethodURL"/>: base64 del JSON con el
        /// threeDSServerTransID y la URL nuestra donde el emisor avisa de que ha terminado.
        /// </summary>
        public string ThreeDSMethodData { get; set; }

        /// <summary>
        /// El formulario de la pasarela de siempre, para cuando no se puede autenticar por REST
        /// (<see cref="Soporta3DS2"/> false). La página lo envía y el cliente paga como hasta
        /// ahora: nunca se le deja sin poder pagar por un problema nuestro.
        /// </summary>
        public FormularioRedsysClasico FormularioClasico { get; set; }

        /// <summary>Motivo para el log cuando no se puede seguir por 3DS2. No se enseña.</summary>
        public string Motivo { get; set; }
    }

    /// <summary>
    /// NestoAPI#181: los datos del POST a la pasarela clásica, para el plan de reserva.
    /// </summary>
    public class FormularioRedsysClasico
    {
        public string Url { get; set; }
        public string Ds_SignatureVersion { get; set; }
        public string Ds_MerchantParameters { get; set; }
        public string Ds_Signature { get; set; }
    }

    public enum EstadoAutenticacion3DS
    {
        /// <summary>Frictionless: autorizado sin preguntar nada al cliente.</summary>
        Autorizado,

        /// <summary>El emisor exige desafío: hay que pintar su pantalla.</summary>
        RetoRequerido,

        Denegado
    }

    /// <summary>
    /// NestoAPI#181: resultado de un paso de autenticación (trataPeticionREST).
    /// </summary>
    public class ResultadoAutenticacion3DS
    {
        public EstadoAutenticacion3DS Estado { get; set; }

        /// <summary>URL del ACS del emisor donde se pinta el desafío. Solo si hay reto.</summary>
        public string AcsUrl { get; set; }

        /// <summary>Challenge Request que hay que postear al ACS. Solo si hay reto.</summary>
        public string Creq { get; set; }

        public string ProtocolVersion { get; set; }
        public string CodigoRespuesta { get; set; }
        public string CodigoAutorizacion { get; set; }
        public string MensajeError { get; set; }
    }

    /// <summary>
    /// NestoAPI#181: todo lo que hace falta para el segundo paso (trataPeticionREST con
    /// threeDSInfo=AuthenticationData).
    /// </summary>
    public class PeticionAutenticacion3DS
    {
        public string ProtocolVersion { get; set; }
        public string ThreeDSServerTransID { get; set; }

        /// <summary>URL nuestra donde el ACS deja el resultado del desafío (el <c>cres</c>).</summary>
        public string NotificationURL { get; set; }

        /// <summary>"Y" si el 3DSMethod se completó, "N" si no dio tiempo o no había.</summary>
        public string ThreeDSCompInd { get; set; }

        public DatosNavegador3DS Navegador { get; set; }

        [JsonIgnore]
        public bool TieneDatosNavegador => Navegador != null;
    }
}
